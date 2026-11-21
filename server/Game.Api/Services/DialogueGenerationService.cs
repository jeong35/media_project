using System.Text.Json;

namespace Game.Api.Services;

public sealed record GeneratedDialogue(string Dialogue, string Emotion, string Feedback,
    IReadOnlyList<string> AcceptedTags, string? CareerFeedback, string Summary);
public sealed record DialogueGenerationResult(GeneratedDialogue Content, string ResponseSource,
    bool FallbackUsed, int AttemptCount);
public sealed record DialogueConversationContext(string Summary = "", string PreviousPlayerText = "",
    string PreviousNpcDialogue = "");
public sealed record DialogueProviderRequest(MockActContent Act, string PlayerText,
    string FailureScenario, int Attempt, IReadOnlyList<string> AllowedTags, string PublicPersonaJson,
    DialogueConversationContext ConversationContext, bool DecisionBridge = false, string ChoiceContext = "");

public interface IDialogueProvider
{
    string ProviderId { get; }
    int MaximumAttempts { get; }
    Task<string> GenerateAsync(DialogueProviderRequest request, CancellationToken cancellationToken = default);
}

public sealed class DialogueProviderException(string message, bool retryable, TimeSpan? retryAfter = null,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool Retryable { get; } = retryable;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class MockDialogueProvider : IDialogueProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ProviderId => "mock";
    public int MaximumAttempts => 2;

    public Task<string> GenerateAsync(DialogueProviderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.FailureScenario == "transient_once" && request.Attempt == 1)
            throw new HttpRequestException("Simulated transient provider failure.");
        if (request.FailureScenario == "timeout")
            throw new TimeoutException("Simulated provider timeout.");
        if (request.FailureScenario == "invalid_payload")
            return Task.FromResult("{ invalid dialogue payload");

        if (request.DecisionBridge)
            return Task.FromResult(JsonSerializer.Serialize(new GeneratedDialogue(request.Act.DecisionBridgeDialogue,
                request.Act.DecisionBridgeEmotion, "대화를 바탕으로 결정 단계로 넘어갑니다.", [], null,
                "결정 단계로 전환했다."), JsonOptions));

        var dialogue = request.Act.MockDialogue;
        return Task.FromResult(JsonSerializer.Serialize(new GeneratedDialogue(dialogue.Dialogue, dialogue.Emotion,
            dialogue.Feedback, dialogue.AcceptedTags, dialogue.CareerFeedback, dialogue.Summary), JsonOptions));
    }
}

public sealed class DialogueGenerationService(IEnumerable<IDialogueProvider> providers, NpcPersonaCatalog personaCatalog,
    LiveApiMetrics liveApiMetrics, ILogger<DialogueGenerationService>? logger = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, IDialogueProvider> providersById = providers
        .ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);

    public Task<DialogueGenerationResult> GenerateAsync(string providerId, MockActContent act,
        string playerText, string failureScenario, IReadOnlyList<string> allowedTags,
        DialogueConversationContext conversationContext, CancellationToken cancellationToken = default) =>
        GenerateCoreAsync(providerId, act, playerText, failureScenario, allowedTags, conversationContext, false, "", cancellationToken);

    public Task<DialogueGenerationResult> GenerateDecisionBridgeAsync(string providerId, MockActContent act,
        string failureScenario, IReadOnlyList<string> allowedTags, DialogueConversationContext conversationContext,
        CancellationToken cancellationToken = default)
    {
        var choiceContext = string.Join("\n", act.Choices.Select(choice => $"- {choice.Label}"));
        return GenerateCoreAsync(providerId, act, "플레이어가 결정을 내리기 위해 대화를 마쳤다.", failureScenario,
            allowedTags, conversationContext, true, choiceContext, cancellationToken);
    }

    private async Task<DialogueGenerationResult> GenerateCoreAsync(string providerId, MockActContent act,
        string playerText, string failureScenario, IReadOnlyList<string> allowedTags,
        DialogueConversationContext conversationContext,
        bool decisionBridge, string choiceContext, CancellationToken cancellationToken)
    {
        if (!providersById.TryGetValue(providerId, out var provider))
            return FixedFallback(act, 0, decisionBridge);

        var isLiveProvider = provider is OpenAiDialogueProvider;
        if (isLiveProvider) liveApiMetrics.RecordProviderRequest(LiveApiOperation.Dialogue);
        var publicPersonaJson = NpcPersonaPromptBuilder.Build(personaCatalog.Get(act.Scene.NpcId));
        var maximumAttempts = Math.Clamp(provider.MaximumAttempts, 1, 3);
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                var raw = await provider.GenerateAsync(
                    new DialogueProviderRequest(act, playerText, failureScenario, attempt, allowedTags, publicPersonaJson,
                        conversationContext, decisionBridge, choiceContext),
                    cancellationToken);
                var generated = JsonSerializer.Deserialize<GeneratedDialogue>(raw, JsonOptions)
                    ?? throw new JsonException("Dialogue payload was empty.");
                Validate(generated, allowedTags);
                return new DialogueGenerationResult(generated, provider.ProviderId, false, attempt);
            }
            catch (DialogueProviderException error) when (!error.Retryable)
            {
                logger?.LogWarning("Dialogue provider {ProviderId} rejected attempt {Attempt}: {Reason}",
                    provider.ProviderId, attempt, error.Message);
                return FixedFallbackWithMetrics(act, attempt, isLiveProvider, liveApiMetrics, decisionBridge);
            }
            catch (Exception error) when (error is HttpRequestException or TimeoutException or JsonException or InvalidDataException)
            {
                logger?.LogWarning("Dialogue provider {ProviderId} produced an unusable attempt {Attempt}: {Reason}",
                    provider.ProviderId, attempt, error.Message);
                // Retry one transient or invalid provider response before selecting fixed content.
            }
            catch (DialogueProviderException error)
            {
                logger?.LogWarning("Dialogue provider {ProviderId} failed attempt {Attempt}: {Reason}",
                    provider.ProviderId, attempt, error.Message);
                if (attempt >= maximumAttempts) continue;
                var delay = error.RetryAfter ?? TimeSpan.FromMilliseconds(100 * attempt);
                if (delay > TimeSpan.FromSeconds(5))
                    return FixedFallbackWithMetrics(act, attempt, isLiveProvider, liveApiMetrics, decisionBridge);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            }
        }

        return FixedFallbackWithMetrics(act, maximumAttempts, isLiveProvider, liveApiMetrics, decisionBridge);
    }

    private static DialogueGenerationResult FixedFallbackWithMetrics(MockActContent act, int attemptCount,
        bool isLiveProvider, LiveApiMetrics metrics, bool decisionBridge)
    {
        if (isLiveProvider) metrics.RecordFallback(LiveApiOperation.Dialogue);
        return FixedFallback(act, attemptCount, decisionBridge);
    }

    private static DialogueGenerationResult FixedFallback(MockActContent act, int attemptCount, bool decisionBridge = false)
    {
        if (decisionBridge)
            return new DialogueGenerationResult(new GeneratedDialogue(act.DecisionBridgeDialogue, act.DecisionBridgeEmotion,
                "대화를 바탕으로 결정 단계로 넘어갑니다.", [], null, "결정 단계로 전환했다."),
                "fixed_fallback", true, attemptCount);

        var fallback = act.MockDialogue;
        return new DialogueGenerationResult(new GeneratedDialogue(fallback.Dialogue, fallback.Emotion,
            fallback.Feedback, fallback.AcceptedTags, fallback.CareerFeedback, fallback.Summary),
            "fixed_fallback", true, attemptCount);
    }

    private static void Validate(GeneratedDialogue dialogue, IReadOnlyList<string> allowedTags)
    {
        if (string.IsNullOrWhiteSpace(dialogue.Dialogue))
            throw new InvalidDataException("Dialogue was empty.");
        if (string.IsNullOrWhiteSpace(dialogue.Emotion))
            throw new InvalidDataException("Emotion was empty.");
        if (string.IsNullOrWhiteSpace(dialogue.Feedback))
            throw new InvalidDataException("Feedback was empty.");
        if (string.IsNullOrWhiteSpace(dialogue.Summary))
            throw new InvalidDataException("Summary was empty.");
        if (dialogue.AcceptedTags is null)
            throw new InvalidDataException("Accepted tags were missing.");
        if (dialogue.CareerFeedback is not null && string.IsNullOrWhiteSpace(dialogue.CareerFeedback))
            throw new InvalidDataException("Career feedback must be null or non-empty.");
        if (dialogue.Dialogue.Length > 500) throw new InvalidDataException("Dialogue exceeded 500 characters.");
        if (dialogue.Emotion.Length > 50) throw new InvalidDataException("Emotion exceeded 50 characters.");
        if (dialogue.Feedback.Length > 300) throw new InvalidDataException("Feedback exceeded 300 characters.");
        if (dialogue.CareerFeedback?.Length > 300)
            throw new InvalidDataException("Career feedback exceeded 300 characters.");
        if (dialogue.Summary.Length > 500) throw new InvalidDataException("Summary exceeded 500 characters.");
        if (dialogue.AcceptedTags.Count > 3 || dialogue.AcceptedTags.Any(tag => !allowedTags.Contains(tag)))
            throw new InvalidDataException("Dialogue contained too many or unsupported tags.");
    }
}

using System.Text.Json;

namespace Game.Api.Services;

public sealed record GeneratedDialogue(string Dialogue, string Emotion, string Feedback,
    IReadOnlyList<string> AcceptedTags, string CareerFeedback, string Summary);
public sealed record DialogueGenerationResult(GeneratedDialogue Content, string ResponseSource,
    bool FallbackUsed, int AttemptCount);
public sealed record DialogueProviderRequest(MockActContent Act, string PlayerText,
    string FailureScenario, int Attempt, IReadOnlyList<string> AllowedTags, string PublicPersonaJson,
    string ConversationSummary = "", bool DecisionBridge = false, string ChoiceContext = "");

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
                request.Act.DecisionBridgeEmotion, "대화를 바탕으로 결정 단계로 넘어갑니다.", [], "",
                "결정 단계로 전환했다."), JsonOptions));

        var dialogue = request.Act.MockDialogue;
        return Task.FromResult(JsonSerializer.Serialize(new GeneratedDialogue(dialogue.Dialogue, dialogue.Emotion,
            dialogue.Feedback, dialogue.AcceptedTags, dialogue.CareerFeedback, dialogue.Summary), JsonOptions));
    }
}

public sealed class DialogueGenerationService(IEnumerable<IDialogueProvider> providers, NpcPersonaCatalog personaCatalog)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, IDialogueProvider> providersById = providers
        .ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);

    public Task<DialogueGenerationResult> GenerateAsync(string providerId, MockActContent act,
        string playerText, string failureScenario, IReadOnlyList<string> allowedTags,
        string conversationSummary = "", CancellationToken cancellationToken = default) =>
        GenerateCoreAsync(providerId, act, playerText, failureScenario, allowedTags, conversationSummary, false, "", cancellationToken);

    public Task<DialogueGenerationResult> GenerateDecisionBridgeAsync(string providerId, MockActContent act,
        string failureScenario, IReadOnlyList<string> allowedTags, string conversationSummary,
        CancellationToken cancellationToken = default)
    {
        var choiceContext = string.Join("\n", act.Choices.Select(choice => $"- {choice.Label}"));
        return GenerateCoreAsync(providerId, act, "플레이어가 결정을 내리기 위해 대화를 마쳤다.", failureScenario,
            allowedTags, conversationSummary, true, choiceContext, cancellationToken);
    }

    private async Task<DialogueGenerationResult> GenerateCoreAsync(string providerId, MockActContent act,
        string playerText, string failureScenario, IReadOnlyList<string> allowedTags, string conversationSummary,
        bool decisionBridge, string choiceContext, CancellationToken cancellationToken)
    {
        if (!providersById.TryGetValue(providerId, out var provider))
            return FixedFallback(act, 0, decisionBridge);

        var publicPersonaJson = NpcPersonaPromptBuilder.Build(personaCatalog.Get(act.Scene.NpcId));
        var maximumAttempts = Math.Clamp(provider.MaximumAttempts, 1, 3);
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                var raw = await provider.GenerateAsync(
                    new DialogueProviderRequest(act, playerText, failureScenario, attempt, allowedTags, publicPersonaJson,
                        conversationSummary, decisionBridge, choiceContext),
                    cancellationToken);
                var generated = JsonSerializer.Deserialize<GeneratedDialogue>(raw, JsonOptions)
                    ?? throw new JsonException("Dialogue payload was empty.");
                Validate(generated, allowedTags);
                return new DialogueGenerationResult(generated, provider.ProviderId, false, attempt);
            }
            catch (DialogueProviderException error) when (!error.Retryable)
            {
                return FixedFallback(act, attempt, decisionBridge);
            }
            catch (Exception error) when (error is HttpRequestException or TimeoutException or JsonException or InvalidDataException)
            {
                // Retry one transient or invalid provider response before selecting fixed content.
            }
            catch (DialogueProviderException error)
            {
                if (attempt >= maximumAttempts) continue;
                var delay = error.RetryAfter ?? TimeSpan.FromMilliseconds(100 * attempt);
                if (delay > TimeSpan.FromSeconds(5)) return FixedFallback(act, attempt, decisionBridge);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            }
        }

        return FixedFallback(act, maximumAttempts, decisionBridge);
    }

    private static DialogueGenerationResult FixedFallback(MockActContent act, int attemptCount, bool decisionBridge = false)
    {
        if (decisionBridge)
            return new DialogueGenerationResult(new GeneratedDialogue(act.DecisionBridgeDialogue, act.DecisionBridgeEmotion,
                "대화를 바탕으로 결정 단계로 넘어갑니다.", [], "", "결정 단계로 전환했다."),
                "fixed_fallback", true, attemptCount);

        var fallback = act.MockDialogue;
        return new DialogueGenerationResult(new GeneratedDialogue(fallback.Dialogue, fallback.Emotion,
            fallback.Feedback, fallback.AcceptedTags, fallback.CareerFeedback, fallback.Summary),
            "fixed_fallback", true, attemptCount);
    }

    private static void Validate(GeneratedDialogue dialogue, IReadOnlyList<string> allowedTags)
    {
        if (string.IsNullOrWhiteSpace(dialogue.Dialogue) || string.IsNullOrWhiteSpace(dialogue.Emotion)
            || string.IsNullOrWhiteSpace(dialogue.Feedback) || string.IsNullOrWhiteSpace(dialogue.CareerFeedback)
            || string.IsNullOrWhiteSpace(dialogue.Summary) || dialogue.AcceptedTags is null)
            throw new InvalidDataException("Dialogue payload did not satisfy the required contract.");
        if (dialogue.Dialogue.Length > 500 || dialogue.Feedback.Length > 300 || dialogue.CareerFeedback.Length > 300
            || dialogue.Summary.Length > 500 || dialogue.AcceptedTags.Count > 3
            || dialogue.AcceptedTags.Any(tag => !allowedTags.Contains(tag)))
            throw new InvalidDataException("Dialogue payload exceeded limits or contained an unsupported tag.");
    }
}

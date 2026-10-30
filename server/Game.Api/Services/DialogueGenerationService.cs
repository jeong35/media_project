using System.Text.Json;

namespace Game.Api.Services;

public sealed record GeneratedDialogue(string Dialogue, string Emotion, string Feedback,
    IReadOnlyList<string> AcceptedTags, string CareerFeedback, string Summary);
public sealed record DialogueGenerationResult(GeneratedDialogue Content, string ResponseSource,
    bool FallbackUsed, int AttemptCount);

public sealed class MockDialogueProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<string> GenerateAsync(MockActContent act, string failureScenario, int attempt)
    {
        if (failureScenario == "transient_once" && attempt == 1)
            throw new HttpRequestException("Simulated transient provider failure.");
        if (failureScenario == "timeout")
            throw new TimeoutException("Simulated provider timeout.");
        if (failureScenario == "invalid_payload")
            return Task.FromResult("{ invalid dialogue payload");

        var dialogue = act.MockDialogue;
        return Task.FromResult(JsonSerializer.Serialize(new GeneratedDialogue(dialogue.Dialogue, dialogue.Emotion,
            dialogue.Feedback, dialogue.AcceptedTags, dialogue.CareerFeedback, dialogue.Summary), JsonOptions));
    }
}

public sealed class DialogueGenerationService(MockDialogueProvider provider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaximumAttempts = 2;

    public async Task<DialogueGenerationResult> GenerateAsync(MockActContent act, string failureScenario)
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var raw = await provider.GenerateAsync(act, failureScenario, attempt);
                var generated = JsonSerializer.Deserialize<GeneratedDialogue>(raw, JsonOptions)
                    ?? throw new JsonException("Dialogue payload was empty.");
                Validate(generated);
                return new DialogueGenerationResult(generated, "mock", false, attempt);
            }
            catch (Exception error) when (error is HttpRequestException or TimeoutException or JsonException or InvalidDataException)
            {
                // A live provider will use the same single-retry boundary before fixed content is selected.
            }
        }

        var fallback = act.MockDialogue;
        return new DialogueGenerationResult(new GeneratedDialogue(fallback.Dialogue, fallback.Emotion,
            fallback.Feedback, fallback.AcceptedTags, fallback.CareerFeedback, fallback.Summary),
            "fixed_fallback", true, MaximumAttempts);
    }

    private static void Validate(GeneratedDialogue dialogue)
    {
        if (string.IsNullOrWhiteSpace(dialogue.Dialogue) || string.IsNullOrWhiteSpace(dialogue.Emotion)
            || string.IsNullOrWhiteSpace(dialogue.Feedback) || string.IsNullOrWhiteSpace(dialogue.CareerFeedback)
            || string.IsNullOrWhiteSpace(dialogue.Summary) || dialogue.AcceptedTags is null)
            throw new InvalidDataException("Dialogue payload did not satisfy the required contract.");
    }
}

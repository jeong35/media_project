using System.Text.Json;

namespace Game.Api.Services;

public sealed record MockSliceContent(string TimeLabel, InitialStateContent InitialState, SceneContent Scene, MockDialogueContent MockDialogue, IReadOnlyList<ChoiceContent> Choices, EndingContent Ending);
public sealed record InitialStateContent(int Military, int Treasury, int Legitimacy, int Ward);
public sealed record SceneContent(string BackgroundId, string NpcId, string NpcName, string NpcEmotion, string Narration, string OpeningDialogue);
public sealed record MockDialogueContent(string Dialogue, string Emotion, string Feedback, IReadOnlyList<string> AcceptedTags, string CareerFeedback, string Summary);
public sealed record ChoiceContent(string ChoiceId, string Label, string ResultNarration, string Feedback, IReadOnlyDictionary<string, int> Changes, IReadOnlyDictionary<string, int> AffinityChanges, IReadOnlyList<string> Flags);
public sealed record EndingContent(string OccupationName, string Title, string TerritoryOutcome, IReadOnlyList<string> Records, IReadOnlyList<string> Traits, string ShareLine);

public static class MockContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static MockSliceContent Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "content", "acts", "act1_mock.json");
        if (!File.Exists(path)) throw new InvalidOperationException($"Mock content was not found: {path}");
        return JsonSerializer.Deserialize<MockSliceContent>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("Mock content could not be parsed.");
    }
}

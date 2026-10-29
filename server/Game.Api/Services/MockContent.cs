using System.Text.Json;

namespace Game.Api.Services;

public sealed record MockGameContent(InitialStateContent InitialState, IReadOnlyDictionary<int, MockActContent> Acts)
{
    public MockActContent GetAct(int act) => Acts.TryGetValue(act, out var content)
        ? content
        : throw new InvalidOperationException($"Mock content for Act {act} was not found.");
}

public sealed record MockActContent(int Act, string TimeLabel, InitialStateContent? InitialState, SceneContent Scene,
    MockDialogueContent MockDialogue, IReadOnlyList<ChoiceContent> Choices, string? Activity, FarmingContent? Farming, EndingContent? Ending);
public sealed record InitialStateContent(int Military, int Treasury, int Legitimacy, int Ward);
public sealed record SceneContent(string BackgroundId, string NpcId, string NpcName, string NpcEmotion, string Narration, string OpeningDialogue);
public sealed record MockDialogueContent(string Dialogue, string Emotion, string Feedback, IReadOnlyList<string> AcceptedTags, string CareerFeedback, string Summary);
public sealed record ChoiceContent(string ChoiceId, string Label, string ResultNarration, string Feedback, IReadOnlyDictionary<string, int> Changes, IReadOnlyDictionary<string, int> AffinityChanges, IReadOnlyList<string> Flags);
public sealed record FarmingContent(string Title, string Instructions, int ObstaclePoints, int SeedPoints, int CompletionThreshold,
    IReadOnlyDictionary<string, FarmingOutcomeContent> WaterOutcomes, IReadOnlyDictionary<string, FarmingOutcomeContent> MethodOutcomes,
    FarmingOutcomeContent CompletionBonus);
public sealed record FarmingOutcomeContent(string Feedback, IReadOnlyDictionary<string, int> Changes, IReadOnlyDictionary<string, int> AffinityChanges);
public sealed record EndingContent(string OccupationName, string Title, string TerritoryOutcome, IReadOnlyList<string> Records, IReadOnlyList<string> Traits, string ShareLine);

public static class MockContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static MockGameContent Load()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "content", "acts");
        if (!Directory.Exists(directory)) throw new InvalidOperationException($"Mock content directory was not found: {directory}");

        var acts = Directory.GetFiles(directory, "act*_mock.json")
            .Select(path => JsonSerializer.Deserialize<MockActContent>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException($"Mock content could not be parsed: {path}"))
            .ToDictionary(content => content.Act);
        if (acts.Count == 0) throw new InvalidOperationException("No Mock act content was found.");

        var initialState = acts.Values.OrderBy(content => content.Act).Select(content => content.InitialState).FirstOrDefault(state => state is not null)
            ?? throw new InvalidOperationException("Mock initial state was not found.");
        return new MockGameContent(initialState, acts);
    }
}

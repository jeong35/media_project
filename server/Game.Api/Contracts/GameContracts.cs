namespace Game.Api.Contracts;

public sealed record HealthResponse(string Status, string Service, string Version, bool DatabaseReady);
public sealed record SessionStartRequest(string Mode = "mock");
public sealed record SceneResponse(string BackgroundId, string NpcId, string NpcName, string NpcEmotion, string Narration);
public sealed record SessionStartResponse(Guid SessionId, int Act, string TimeLabel, SceneResponse Scene, string OpeningDialogue, int DialogueRemaining);
public sealed record DialogueRequest(string NpcId, string Text, string IdempotencyKey);
public sealed record DialogueResponse(string Dialogue, string Emotion, string Feedback, IReadOnlyList<string> AcceptedTags, string CareerFeedback, int DialogueRemaining, bool ChoicesAvailable);
public sealed record ChoiceOptionResponse(string ChoiceId, string Label);
public sealed record ChoicesResponse(IReadOnlyList<ChoiceOptionResponse> Choices);
public sealed record ChoiceRequest(string ChoiceId, string IdempotencyKey);
public sealed record GameStateResponse(int Military, int Treasury, int Legitimacy, int Ward);
public sealed record EndingResponse(string OccupationName, string Title, string TerritoryOutcome, IReadOnlyList<string> Records, IReadOnlyList<string> Traits, string ShareLine);
public sealed record NextActResponse(int Act, string TimeLabel, SceneResponse Scene, string OpeningDialogue, int DialogueRemaining);
public sealed record NextActivityResponse(string ActivityId, string Title, string Instructions);
public sealed record ChoiceResultResponse(string ResultNarration, string Feedback, IReadOnlyDictionary<string, int> AppliedChanges,
    GameStateResponse State, NextActResponse? NextAct, NextActivityResponse? NextActivity, EndingResponse? Ending);
public sealed record FarmingResultRequest(int RemovedObstacles, int CorrectSeeds, string WaterTarget, string Method, string IdempotencyKey);
public sealed record FarmingResultResponse(int Score, string Feedback, IReadOnlyDictionary<string, int> AppliedChanges,
    GameStateResponse State, EndingResponse Ending);
public sealed record SessionSnapshotResponse(Guid SessionId, string Status, int Act, int Turn, string Phase, GameStateResponse State, EndingResponse? Ending);
public sealed record ErrorResponse(string Code, string Message, bool Retryable, string RequestId);
public sealed record ErrorEnvelope(ErrorResponse Error);

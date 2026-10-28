using System.Text.Json;
using Game.Api.Contracts;
using Game.Api.Data;
using Game.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace Game.Api.Services;

public sealed class MockGameService(GameDbContext database, MockSliceContent content)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] TraitIds = ["kind", "ruthless", "persuasive", "observant", "pragmatic", "reckless", "eccentric", "threatening"];
    private static readonly string[] AffinityIds = ["martial", "arcane", "diplomacy", "commerce", "leadership", "craft"];

    public async Task<SessionStartResponse> StartSessionAsync(string mode)
    {
        if (!string.Equals(mode, "mock", StringComparison.OrdinalIgnoreCase))
            throw new GameRuleException("UNSUPPORTED_MODE", "현재 세로 슬라이스는 Mock 모드만 지원합니다.");

        var now = DateTimeOffset.UtcNow;
        var session = new GameSession
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Mode = "Mock",
            Military = content.InitialState.Military, Treasury = content.InitialState.Treasury,
            Legitimacy = content.InitialState.Legitimacy, Ward = content.InitialState.Ward
        };
        database.Sessions.Add(session);
        database.NpcStates.Add(new NpcState { SessionId = session.Id, NpcId = content.Scene.NpcId, Trust = 20 });
        database.TraitScores.AddRange(TraitIds.Select(id => new TraitScore { SessionId = session.Id, TraitId = id }));
        database.CareerAffinities.AddRange(AffinityIds.Select(id => new CareerAffinity { SessionId = session.Id, AffinityId = id }));
        database.GameEvents.Add(CreateEvent(session.Id, 1, "SessionStarted", new { mode = "Mock" }));
        await database.SaveChangesAsync();

        return new SessionStartResponse(session.Id, 1, content.TimeLabel,
            new SceneResponse(content.Scene.BackgroundId, content.Scene.NpcId, content.Scene.NpcName, content.Scene.NpcEmotion, content.Scene.Narration),
            content.Scene.OpeningDialogue, 1);
    }

    public async Task<SessionSnapshotResponse> GetSessionAsync(Guid sessionId)
    {
        var session = await FindSessionAsync(sessionId);
        var ending = session.EndingJson is null ? null : JsonSerializer.Deserialize<EndingResponse>(session.EndingJson, JsonOptions);
        return new SessionSnapshotResponse(session.Id, session.Status, session.CurrentAct, session.CurrentTurn, session.Phase, ToStateResponse(session), ending);
    }

    public async Task<DialogueResponse> SubmitDialogueAsync(Guid sessionId, DialogueRequest request)
    {
        ValidateIdempotencyKey(request.IdempotencyKey);
        var duplicate = await FindExistingResponseAsync<DialogueResponse>(sessionId, request.IdempotencyKey, "DialogueGenerated");
        if (duplicate is not null) return duplicate;

        var session = await FindSessionAsync(sessionId);
        if (session.Phase != "AwaitingDialogue") throw new GameRuleException("INVALID_TURN", "현재는 자유 대화를 제출할 수 없습니다.");
        if (request.NpcId != content.Scene.NpcId) throw new GameRuleException("INVALID_NPC", "현재 장면의 NPC와 일치하지 않습니다.");
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 300) throw new GameRuleException("INVALID_TEXT", "대화는 1자 이상 300자 이하로 입력해 주세요.");

        var response = new DialogueResponse(content.MockDialogue.Dialogue, content.MockDialogue.Emotion, content.MockDialogue.Feedback,
            content.MockDialogue.AcceptedTags, content.MockDialogue.CareerFeedback, 0, true);
        var npc = await database.NpcStates.SingleAsync(state => state.SessionId == sessionId && state.NpcId == content.Scene.NpcId);
        npc.Trust = Math.Clamp(npc.Trust + 2, 0, 100);
        npc.ConversationSummary = content.MockDialogue.Summary;
        var kind = await database.TraitScores.SingleAsync(score => score.SessionId == sessionId && score.TraitId == "kind");
        kind.Score = Math.Clamp(kind.Score + 1, 0, 10);
        session.CurrentTurn = 1;
        session.Phase = "AwaitingChoice";
        session.UpdatedAt = DateTimeOffset.UtcNow;
        database.GameEvents.Add(CreateEvent(sessionId, await NextSequenceAsync(sessionId), "DialogueGenerated", response, request.IdempotencyKey));
        await database.SaveChangesAsync();
        return response;
    }

    public async Task<ChoicesResponse> GetChoicesAsync(Guid sessionId)
    {
        var session = await FindSessionAsync(sessionId);
        if (session.Phase != "AwaitingChoice") throw new GameRuleException("CHOICES_UNAVAILABLE", "아직 선택지를 확인할 수 없습니다.");
        return new ChoicesResponse(content.Choices.Select(choice => new ChoiceOptionResponse(choice.ChoiceId, choice.Label)).ToArray());
    }

    public async Task<ChoiceResultResponse> SubmitChoiceAsync(Guid sessionId, ChoiceRequest request)
    {
        ValidateIdempotencyKey(request.IdempotencyKey);
        var duplicate = await FindExistingResponseAsync<ChoiceResultResponse>(sessionId, request.IdempotencyKey, "ChoiceSelected");
        if (duplicate is not null) return duplicate;

        var session = await FindSessionAsync(sessionId);
        if (session.Phase != "AwaitingChoice") throw new GameRuleException("INVALID_TURN", "현재는 선택지를 제출할 수 없습니다.");
        var choice = content.Choices.SingleOrDefault(candidate => candidate.ChoiceId == request.ChoiceId)
            ?? throw new GameRuleException("INVALID_CHOICE", "허용되지 않은 선택지입니다.");

        ApplyStateChanges(session, choice.Changes);
        await ApplyAffinityChangesAsync(sessionId, choice.AffinityChanges);
        var ending = new EndingResponse(content.Ending.OccupationName, content.Ending.Title, content.Ending.TerritoryOutcome,
            [choice.ResultNarration, .. content.Ending.Records.Take(2)], content.Ending.Traits, content.Ending.ShareLine);
        var response = new ChoiceResultResponse(choice.ResultNarration, choice.Feedback, choice.Changes, ToStateResponse(session), ending);
        session.Status = "Completed";
        session.Phase = "Completed";
        session.CurrentTurn = 2;
        session.SelectedChoiceId = choice.ChoiceId;
        session.EndingJson = JsonSerializer.Serialize(ending, JsonOptions);
        session.UpdatedAt = DateTimeOffset.UtcNow;
        database.GameEvents.Add(CreateEvent(sessionId, await NextSequenceAsync(sessionId), "ChoiceSelected", response, request.IdempotencyKey));
        await database.SaveChangesAsync();
        return response;
    }

    private async Task<GameSession> FindSessionAsync(Guid id) =>
        await database.Sessions.SingleOrDefaultAsync(session => session.Id == id) ?? throw new SessionNotFoundException();

    private async Task<int> NextSequenceAsync(Guid id) =>
        (await database.GameEvents.Where(e => e.SessionId == id).MaxAsync(e => (int?)e.Sequence) ?? 0) + 1;

    private async Task<T?> FindExistingResponseAsync<T>(Guid sessionId, string key, string eventType) where T : class
    {
        var gameEvent = await database.GameEvents.SingleOrDefaultAsync(candidate =>
            candidate.SessionId == sessionId && candidate.IdempotencyKey == key && candidate.EventType == eventType);
        return gameEvent is null ? null : JsonSerializer.Deserialize<T>(gameEvent.PayloadJson, JsonOptions);
    }

    private static GameEvent CreateEvent(Guid sessionId, int sequence, string eventType, object payload, string? key = null) => new()
    {
        Id = Guid.NewGuid(), SessionId = sessionId, Sequence = sequence, EventType = eventType, Act = 1,
        PayloadJson = JsonSerializer.Serialize(payload, JsonOptions), CreatedAt = DateTimeOffset.UtcNow, IdempotencyKey = key
    };

    private static void ValidateIdempotencyKey(string key)
    {
        if (!Guid.TryParse(key, out _)) throw new GameRuleException("INVALID_IDEMPOTENCY_KEY", "idempotencyKey는 UUID 형식이어야 합니다.");
    }

    private static void ApplyStateChanges(GameSession session, IReadOnlyDictionary<string, int> changes)
    {
        foreach (var (state, amount) in changes)
        {
            switch (state)
            {
                case "military": session.Military = Math.Clamp(session.Military + amount, 0, 100); break;
                case "treasury": session.Treasury = Math.Clamp(session.Treasury + amount, 0, 100); break;
                case "legitimacy": session.Legitimacy = Math.Clamp(session.Legitimacy + amount, 0, 100); break;
                case "ward": session.Ward = Math.Clamp(session.Ward + amount, 0, 100); break;
                default: throw new InvalidOperationException($"Unknown state change: {state}");
            }
        }
    }

    private async Task ApplyAffinityChangesAsync(Guid sessionId, IReadOnlyDictionary<string, int> changes)
    {
        foreach (var (id, amount) in changes)
        {
            var affinity = await database.CareerAffinities.SingleAsync(candidate => candidate.SessionId == sessionId && candidate.AffinityId == id);
            affinity.Score = Math.Clamp(affinity.Score + amount, 0, 100);
            affinity.LastChangedAct = 1;
        }
    }

    private static GameStateResponse ToStateResponse(GameSession session) =>
        new(session.Military, session.Treasury, session.Legitimacy, session.Ward);
}

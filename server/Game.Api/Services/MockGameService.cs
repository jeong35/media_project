using System.Text.Json;
using Game.Api.Contracts;
using Game.Api.Data;
using Game.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace Game.Api.Services;

public sealed class MockGameService(GameDbContext database, MockGameContent content)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] TraitIds = ["kind", "ruthless", "persuasive", "observant", "pragmatic", "reckless", "eccentric", "threatening"];
    private static readonly string[] AffinityIds = ["martial", "arcane", "diplomacy", "commerce", "leadership", "craft"];

    public async Task<SessionStartResponse> StartSessionAsync(string mode)
    {
        if (!string.Equals(mode, "mock", StringComparison.OrdinalIgnoreCase))
            throw new GameRuleException("UNSUPPORTED_MODE", "현재 세로 슬라이스는 Mock 모드만 지원합니다.");

        var act = content.GetAct(1);
        var now = DateTimeOffset.UtcNow;
        var session = new GameSession
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Mode = "Mock",
            Military = content.InitialState.Military, Treasury = content.InitialState.Treasury,
            Legitimacy = content.InitialState.Legitimacy, Ward = content.InitialState.Ward
        };
        database.Sessions.Add(session);
        database.NpcStates.Add(new NpcState { SessionId = session.Id, NpcId = act.Scene.NpcId, Trust = 20 });
        database.TraitScores.AddRange(TraitIds.Select(id => new TraitScore { SessionId = session.Id, TraitId = id }));
        database.CareerAffinities.AddRange(AffinityIds.Select(id => new CareerAffinity { SessionId = session.Id, AffinityId = id }));
        database.GameEvents.Add(CreateEvent(session.Id, 1, "SessionStarted", new { mode = "Mock" }));
        await database.SaveChangesAsync();

        return new SessionStartResponse(session.Id, 1, act.TimeLabel,
            ToSceneResponse(act.Scene), act.Scene.OpeningDialogue, 1);
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
        var act = content.GetAct(session.CurrentAct);
        if (session.Phase != "AwaitingDialogue") throw new GameRuleException("INVALID_TURN", "현재는 자유 대화를 제출할 수 없습니다.");
        if (request.NpcId != act.Scene.NpcId) throw new GameRuleException("INVALID_NPC", "현재 장면의 NPC와 일치하지 않습니다.");
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 300) throw new GameRuleException("INVALID_TEXT", "대화는 1자 이상 300자 이하로 입력해 주세요.");

        var response = new DialogueResponse(act.MockDialogue.Dialogue, act.MockDialogue.Emotion, act.MockDialogue.Feedback,
            act.MockDialogue.AcceptedTags, act.MockDialogue.CareerFeedback, 0, true);
        var npc = await database.NpcStates.SingleAsync(state => state.SessionId == sessionId && state.NpcId == act.Scene.NpcId);
        npc.Trust = Math.Clamp(npc.Trust + 2, 0, 100);
        npc.ConversationSummary = act.MockDialogue.Summary;
        foreach (var tag in act.MockDialogue.AcceptedTags.Where(TraitIds.Contains))
        {
            var trait = await database.TraitScores.SingleAsync(score => score.SessionId == sessionId && score.TraitId == tag);
            trait.Score = Math.Clamp(trait.Score + 1, 0, 10);
        }
        session.CurrentTurn += 1;
        session.Phase = "AwaitingChoice";
        session.UpdatedAt = DateTimeOffset.UtcNow;
        database.GameEvents.Add(CreateEvent(sessionId, await NextSequenceAsync(sessionId), "DialogueGenerated", session.CurrentAct, response, request.IdempotencyKey));
        await database.SaveChangesAsync();
        return response;
    }

    public async Task<ChoicesResponse> GetChoicesAsync(Guid sessionId)
    {
        var session = await FindSessionAsync(sessionId);
        if (session.Phase != "AwaitingChoice") throw new GameRuleException("CHOICES_UNAVAILABLE", "아직 선택지를 확인할 수 없습니다.");
        var act = content.GetAct(session.CurrentAct);
        return new ChoicesResponse(act.Choices.Select(choice => new ChoiceOptionResponse(choice.ChoiceId, choice.Label)).ToArray());
    }

    public async Task<ChoiceResultResponse> SubmitChoiceAsync(Guid sessionId, ChoiceRequest request)
    {
        ValidateIdempotencyKey(request.IdempotencyKey);
        var duplicate = await FindExistingResponseAsync<ChoiceResultResponse>(sessionId, request.IdempotencyKey, "ChoiceSelected");
        if (duplicate is not null) return duplicate;

        var session = await FindSessionAsync(sessionId);
        var selectedAct = session.CurrentAct;
        var act = content.GetAct(session.CurrentAct);
        if (session.Phase != "AwaitingChoice") throw new GameRuleException("INVALID_TURN", "현재는 선택지를 제출할 수 없습니다.");
        var choice = act.Choices.SingleOrDefault(candidate => candidate.ChoiceId == request.ChoiceId)
            ?? throw new GameRuleException("INVALID_CHOICE", "허용되지 않은 선택지입니다.");

        ApplyStateChanges(session, choice.Changes);
        await ApplyAffinityChangesAsync(sessionId, choice.AffinityChanges, selectedAct);
        session.SelectedChoiceId = choice.ChoiceId;
        session.UpdatedAt = DateTimeOffset.UtcNow;

        EndingResponse? ending = null;
        NextActResponse? nextAct = null;
        if (act.Ending is not null)
        {
            ending = new EndingResponse(act.Ending.OccupationName, act.Ending.Title, act.Ending.TerritoryOutcome,
                [choice.ResultNarration, .. act.Ending.Records.Take(2)], act.Ending.Traits, act.Ending.ShareLine);
            session.Status = "Completed";
            session.Phase = "Completed";
            session.EndingJson = JsonSerializer.Serialize(ending, JsonOptions);
        }
        else
        {
            var nextContent = content.GetAct(session.CurrentAct + 1);
            session.CurrentAct = nextContent.Act;
            session.CurrentTurn = 0;
            session.Phase = "AwaitingDialogue";
            if (!await database.NpcStates.AnyAsync(state => state.SessionId == sessionId && state.NpcId == nextContent.Scene.NpcId))
                database.NpcStates.Add(new NpcState { SessionId = sessionId, NpcId = nextContent.Scene.NpcId, Trust = 20 });
            nextAct = new NextActResponse(nextContent.Act, nextContent.TimeLabel, ToSceneResponse(nextContent.Scene), nextContent.Scene.OpeningDialogue, 1);
        }

        var response = new ChoiceResultResponse(choice.ResultNarration, choice.Feedback, choice.Changes, ToStateResponse(session), nextAct, ending);
        database.GameEvents.Add(CreateEvent(sessionId, await NextSequenceAsync(sessionId), "ChoiceSelected", selectedAct, response, request.IdempotencyKey));
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

    private static SceneResponse ToSceneResponse(SceneContent scene) =>
        new(scene.BackgroundId, scene.NpcId, scene.NpcName, scene.NpcEmotion, scene.Narration);

    private static GameEvent CreateEvent(Guid sessionId, int sequence, string eventType, object payload, string? key = null) =>
        CreateEvent(sessionId, sequence, eventType, 1, payload, key);

    private static GameEvent CreateEvent(Guid sessionId, int sequence, string eventType, int act, object payload, string? key = null) => new()
    {
        Id = Guid.NewGuid(), SessionId = sessionId, Sequence = sequence, EventType = eventType, Act = act,
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

    private async Task ApplyAffinityChangesAsync(Guid sessionId, IReadOnlyDictionary<string, int> changes, int act)
    {
        foreach (var (id, amount) in changes)
        {
            var affinity = await database.CareerAffinities.SingleAsync(candidate => candidate.SessionId == sessionId && candidate.AffinityId == id);
            affinity.Score = Math.Clamp(affinity.Score + amount, 0, 100);
            affinity.LastChangedAct = act;
        }
    }

    private static GameStateResponse ToStateResponse(GameSession session) =>
        new(session.Military, session.Treasury, session.Legitimacy, session.Ward);
}

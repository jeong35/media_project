using System.Text.Json;
using Game.Api.Contracts;
using Game.Api.Data;
using Game.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace Game.Api.Services;

public sealed class MockGameService(GameDbContext database, MockGameContent content, DialogueGenerationService dialogueGeneration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] TraitIds = ["kind", "ruthless", "persuasive", "observant", "pragmatic", "reckless", "eccentric", "threatening"];
    private static readonly string[] AffinityIds = ["martial", "arcane", "diplomacy", "commerce", "leadership", "craft"];
    private static readonly string[] FailureScenarios = ["none", "transient_once", "invalid_payload", "timeout"];
    private sealed record FinalChoiceTimerEvent(DateTimeOffset DeadlineUtc);

    public async Task<SessionStartResponse> StartSessionAsync(string mode, string failureScenario = "none")
    {
        var providerMode = mode.ToLowerInvariant();
        if (providerMode is not ("mock" or "live"))
            throw new GameRuleException("UNSUPPORTED_MODE", "mode는 mock 또는 live여야 합니다.");
        if (!FailureScenarios.Contains(failureScenario))
            throw new GameRuleException("INVALID_FAILURE_SCENARIO", "허용되지 않은 Mock 실패 시나리오입니다.");
        if (providerMode == "live" && failureScenario != "none")
            throw new GameRuleException("INVALID_FAILURE_SCENARIO", "Live 모드에는 Mock 실패 시나리오를 사용할 수 없습니다.");

        var act = content.GetAct(1);
        var now = DateTimeOffset.UtcNow;
        var session = new GameSession
        {
            Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
            Mode = providerMode == "live" ? "Live" : failureScenario == "none" ? "Mock" : $"Mock:{failureScenario}",
            Military = content.InitialState.Military, Treasury = content.InitialState.Treasury,
            Legitimacy = content.InitialState.Legitimacy, Ward = content.InitialState.Ward
        };
        database.Sessions.Add(session);
        database.NpcStates.Add(new NpcState { SessionId = session.Id, NpcId = act.Scene.NpcId, Trust = 20 });
        database.TraitScores.AddRange(TraitIds.Select(id => new TraitScore { SessionId = session.Id, TraitId = id }));
        database.CareerAffinities.AddRange(AffinityIds.Select(id => new CareerAffinity { SessionId = session.Id, AffinityId = id }));
        database.GameEvents.Add(CreateEvent(session.Id, 1, "SessionStarted", new { mode = "Mock", failureScenario }));
        await database.SaveChangesAsync();

        return new SessionStartResponse(session.Id, 1, act.TimeLabel,
            ToSceneResponse(act.Scene), act.Scene.OpeningDialogue, 1);
    }

    public async Task<SessionSnapshotResponse> GetSessionAsync(Guid sessionId)
    {
        var session = await FindSessionAsync(sessionId);
        var act = content.GetAct(session.CurrentAct);
        var ending = session.EndingJson is null ? null : JsonSerializer.Deserialize<EndingResponse>(session.EndingJson, JsonOptions);
        var nextActivity = session.Phase == "AwaitingFarming" && act.Farming is not null
            ? new NextActivityResponse("farming", act.Farming.Title, act.Farming.Instructions)
            : null;
        var farmingProgress = session.Phase == "AwaitingFarming"
            ? await GetLatestFarmingProgressAsync(sessionId)
            : null;
        return new SessionSnapshotResponse(session.Id, session.Status, session.CurrentAct, session.CurrentTurn, session.Phase,
            ToStateResponse(session), ending, act.TimeLabel, ToSceneResponse(act.Scene), act.Scene.OpeningDialogue,
            act.MockDialogue.Dialogue, nextActivity, farmingProgress);
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

        var generated = await dialogueGeneration.GenerateAsync(GetProviderId(session.Mode), act, request.Text,
            GetFailureScenario(session.Mode), TraitIds);
        var dialogue = generated.Content;
        var response = new DialogueResponse(dialogue.Dialogue, dialogue.Emotion, dialogue.Feedback,
            dialogue.AcceptedTags, dialogue.CareerFeedback, 0, true, generated.ResponseSource,
            generated.FallbackUsed, generated.AttemptCount);
        var npc = await database.NpcStates.SingleAsync(state => state.SessionId == sessionId && state.NpcId == act.Scene.NpcId);
        npc.Trust = Math.Clamp(npc.Trust + 2, 0, 100);
        npc.ConversationSummary = dialogue.Summary;
        foreach (var tag in dialogue.AcceptedTags.Where(TraitIds.Contains))
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
        double? remainingSeconds = null;
        if (act.ChoiceTimeLimitSeconds is not null)
        {
            var timer = await GetFinalChoiceTimerAsync(sessionId, act, true);
            remainingSeconds = Math.Max(0, (timer!.DeadlineUtc - DateTimeOffset.UtcNow).TotalSeconds);
        }
        return new ChoicesResponse(act.Choices.Select(choice => new ChoiceOptionResponse(choice.ChoiceId, choice.Label)).ToArray(),
            act.ChoiceTimeLimitSeconds, act.TimeoutChoiceId, remainingSeconds);
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
        var timedOut = request.TimedOut;
        if (act.ChoiceTimeLimitSeconds is not null)
        {
            var timer = await GetFinalChoiceTimerAsync(sessionId, act, false);
            if (timer is not null && timer.DeadlineUtc <= DateTimeOffset.UtcNow)
            {
                choice = act.Choices.Single(candidate => candidate.ChoiceId == act.TimeoutChoiceId);
                timedOut = true;
            }
        }
        if (timedOut && (act.ChoiceTimeLimitSeconds is null || choice.ChoiceId != act.TimeoutChoiceId))
            throw new GameRuleException("INVALID_TIMEOUT_CHOICE", "시간 초과 시 지정된 자동 선택지만 제출할 수 있습니다.");

        ApplyStateChanges(session, choice.Changes);
        await ApplyAffinityChangesAsync(sessionId, choice.AffinityChanges, selectedAct);
        session.SelectedChoiceId = choice.ChoiceId;
        session.UpdatedAt = DateTimeOffset.UtcNow;

        EndingResponse? ending = null;
        NextActResponse? nextAct = null;
        NextActivityResponse? nextActivity = null;
        if (act.FinaleRules is not null)
        {
            ending = await BuildFinalEndingAsync(session, act, choice, timedOut);
            session.Status = "Completed";
            session.Phase = "Completed";
            session.EndingJson = JsonSerializer.Serialize(ending, JsonOptions);
        }
        else if (act.Activity == "farming" && act.Farming is not null)
        {
            session.Phase = "AwaitingFarming";
            nextActivity = new NextActivityResponse("farming", act.Farming.Title, act.Farming.Instructions);
        }
        else if (act.Ending is not null)
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

        var response = new ChoiceResultResponse(choice.ResultNarration, choice.Feedback, choice.Changes, ToStateResponse(session), nextAct, nextActivity, ending);
        database.GameEvents.Add(CreateEvent(sessionId, await NextSequenceAsync(sessionId), "ChoiceSelected", selectedAct, response, request.IdempotencyKey));
        await database.SaveChangesAsync();
        return response;
    }

    public async Task<FarmingResultResponse> SubmitFarmingAsync(Guid sessionId, FarmingResultRequest request)
    {
        ValidateIdempotencyKey(request.IdempotencyKey);
        var duplicate = await FindExistingResponseAsync<FarmingResultResponse>(sessionId, request.IdempotencyKey, "FarmingCompleted");
        if (duplicate is not null) return duplicate;

        var session = await FindSessionAsync(sessionId);
        var selectedAct = session.CurrentAct;
        var act = content.GetAct(selectedAct);
        var farming = act.Farming ?? throw new GameRuleException("FARMING_UNAVAILABLE", "현재 장면에는 농사 활동이 없습니다.");
        if (session.Phase != "AwaitingFarming") throw new GameRuleException("INVALID_TURN", "현재는 농사 결과를 제출할 수 없습니다.");
        if (request.RemovedObstacles is < 0 or > 3 || request.CorrectSeeds is < 0 or > 3)
            throw new GameRuleException("INVALID_FARMING_RESULT", "농사 행동 횟수가 허용 범위를 벗어났습니다.");
        if (!farming.WaterOutcomes.TryGetValue(request.WaterTarget, out var waterOutcome))
            throw new GameRuleException("INVALID_WATER_TARGET", "허용되지 않은 물 배분 대상입니다.");
        if (!farming.MethodOutcomes.TryGetValue(request.Method, out var methodOutcome))
            throw new GameRuleException("INVALID_FARMING_METHOD", "허용되지 않은 작업 방식입니다.");

        var score = request.RemovedObstacles * farming.ObstaclePoints + request.CorrectSeeds * farming.SeedPoints;
        var changes = MergeChanges(waterOutcome.Changes, methodOutcome.Changes,
            score >= farming.CompletionThreshold ? farming.CompletionBonus.Changes : EmptyChanges);
        var affinities = MergeChanges(waterOutcome.AffinityChanges, methodOutcome.AffinityChanges,
            score >= farming.CompletionThreshold ? farming.CompletionBonus.AffinityChanges : EmptyChanges);
        ApplyStateChanges(session, changes);
        await ApplyAffinityChangesAsync(sessionId, affinities, selectedAct);

        var feedbackParts = new List<string> { waterOutcome.Feedback, methodOutcome.Feedback };
        if (score >= farming.CompletionThreshold) feedbackParts.Add(farming.CompletionBonus.Feedback);
        EndingResponse? ending = null;
        NextActResponse? nextAct = null;
        if (act.Ending is not null)
        {
            ending = new EndingResponse(act.Ending.OccupationName, act.Ending.Title, act.Ending.TerritoryOutcome,
                [$"농사 점수 {score}점으로 마력보리밭을 완성했다.", .. act.Ending.Records.Take(2)], act.Ending.Traits, act.Ending.ShareLine);
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
        var response = new FarmingResultResponse(score, string.Join(" ", feedbackParts), changes, ToStateResponse(session), nextAct, ending);

        session.UpdatedAt = DateTimeOffset.UtcNow;
        database.GameEvents.Add(CreateEvent(sessionId, await NextSequenceAsync(sessionId), "FarmingCompleted", selectedAct, response, request.IdempotencyKey));
        await database.SaveChangesAsync();
        return response;
    }

    public async Task<FarmingProgressResponse> SaveFarmingProgressAsync(Guid sessionId, FarmingProgressRequest request)
    {
        ValidateIdempotencyKey(request.IdempotencyKey);
        var duplicate = await FindExistingResponseAsync<FarmingProgressResponse>(sessionId, request.IdempotencyKey, "FarmingProgressSaved");
        if (duplicate is not null) return duplicate;

        var session = await FindSessionAsync(sessionId);
        var act = content.GetAct(session.CurrentAct);
        var farming = act.Farming ?? throw new GameRuleException("FARMING_UNAVAILABLE", "현재 장면에는 농사 활동이 없습니다.");
        if (session.Phase != "AwaitingFarming") throw new GameRuleException("INVALID_TURN", "현재는 농사 진행을 저장할 수 없습니다.");
        ValidateProgressIndexes(request.RemovedObstacleIndexes, 3, "장애물");
        ValidateProgressIndexes(request.PlantedSeedIndexes, 4, "씨앗");
        if (!farming.WaterOutcomes.ContainsKey(request.WaterTarget))
            throw new GameRuleException("INVALID_WATER_TARGET", "허용되지 않은 물 배분 대상입니다.");
        if (!farming.MethodOutcomes.ContainsKey(request.Method))
            throw new GameRuleException("INVALID_FARMING_METHOD", "허용되지 않은 작업 방식입니다.");

        var response = new FarmingProgressResponse(request.RemovedObstacleIndexes.Order().ToArray(),
            request.PlantedSeedIndexes.Order().ToArray(), request.WaterTarget, request.Method);
        database.GameEvents.Add(CreateEvent(sessionId, await NextSequenceAsync(sessionId), "FarmingProgressSaved",
            session.CurrentAct, response, request.IdempotencyKey));
        session.UpdatedAt = DateTimeOffset.UtcNow;
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

    private async Task<FarmingProgressResponse?> GetLatestFarmingProgressAsync(Guid sessionId)
    {
        var gameEvent = await database.GameEvents.Where(candidate =>
                candidate.SessionId == sessionId && candidate.EventType == "FarmingProgressSaved")
            .OrderByDescending(candidate => candidate.Sequence)
            .FirstOrDefaultAsync();
        return gameEvent is null ? null : JsonSerializer.Deserialize<FarmingProgressResponse>(gameEvent.PayloadJson, JsonOptions);
    }

    private async Task<FinalChoiceTimerEvent?> GetFinalChoiceTimerAsync(Guid sessionId, MockActContent act, bool startIfMissing)
    {
        var gameEvent = await database.GameEvents.SingleOrDefaultAsync(candidate =>
            candidate.SessionId == sessionId && candidate.EventType == "FinalChoiceTimerStarted");
        if (gameEvent is not null)
            return JsonSerializer.Deserialize<FinalChoiceTimerEvent>(gameEvent.PayloadJson, JsonOptions);
        if (!startIfMissing || act.ChoiceTimeLimitSeconds is null) return null;

        var timer = new FinalChoiceTimerEvent(DateTimeOffset.UtcNow.AddSeconds(act.ChoiceTimeLimitSeconds.Value));
        database.GameEvents.Add(CreateEvent(sessionId, await NextSequenceAsync(sessionId), "FinalChoiceTimerStarted", act.Act, timer));
        await database.SaveChangesAsync();
        return timer;
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

    private static void ValidateProgressIndexes(IReadOnlyList<int> indexes, int count, string label)
    {
        if (indexes.Count != indexes.Distinct().Count() || indexes.Any(index => index < 0 || index >= count))
            throw new GameRuleException("INVALID_FARMING_PROGRESS", $"{label} 진행 정보가 허용 범위를 벗어났습니다.");
    }

    private static string GetFailureScenario(string mode) =>
        mode.StartsWith("Mock:", StringComparison.Ordinal) ? mode[5..] : "none";

    private static string GetProviderId(string mode) =>
        mode.StartsWith("Live", StringComparison.OrdinalIgnoreCase) ? "openai" : "mock";

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

    private static readonly IReadOnlyDictionary<string, int> EmptyChanges = new Dictionary<string, int>();

    private async Task<EndingResponse> BuildFinalEndingAsync(GameSession session, MockActContent act, ChoiceContent choice, bool timedOut)
    {
        var rules = act.FinaleRules ?? throw new InvalidOperationException("Finale rules were not found.");
        var intended = choice.FinalOutcome ?? throw new InvalidOperationException($"Final outcome was not found: {choice.ChoiceId}");
        var state = ToStateResponse(session);
        var preparedCount = new[] { state.Military, state.Treasury, state.Legitimacy, state.Ward }
            .Count(value => value >= rules.PreparedStateThreshold);
        var requirementsMet = intended.Requirements.All(requirement => GetStateValue(state, requirement.Key) >= requirement.Value);
        var evacuation = choice.ChoiceId == act.TimeoutChoiceId;
        var intendedSucceeded = evacuation || requirementsMet && preparedCount >= rules.RequiredPreparedStates;
        var outcome = intendedSucceeded
            ? intended
            : state.Legitimacy >= rules.DamagedSurvivalLegitimacy ? rules.DamagedSurvival : rules.Destroyed;
        var affinityScores = await database.CareerAffinities
            .Where(affinity => affinity.SessionId == session.Id)
            .ToArrayAsync();
        var topAffinities = affinityScores
            .OrderByDescending(affinity => affinity.Score)
            .ThenByDescending(affinity => affinity.LastChangedAct)
            .ThenBy(affinity => affinity.AffinityId)
            .Take(2)
            .ToArray();
        var careerRules = rules.CareerRules;
        var baseRank = careerRules.RankByTerritoryFate.TryGetValue(outcome.TerritoryFateId, out var mappedRank)
            ? mappedRank
            : throw new InvalidOperationException($"Career rank was not found for fate: {outcome.TerritoryFateId}");
        var topScore = topAffinities.FirstOrDefault()?.Score ?? 0;
        var careerRank = baseRank == "accomplished" && preparedCount >= careerRules.LegendaryPreparedStates
            && topScore >= careerRules.LegendaryAffinityScore ? "legendary" : baseRank;
        var baseTone = careerRules.ToneByCareerRank.TryGetValue(careerRank, out var mappedTone)
            ? mappedTone
            : throw new InvalidOperationException($"Ending tone was not found for rank: {careerRank}");
        var affinityScoreById = affinityScores.ToDictionary(affinity => affinity.AffinityId, affinity => affinity.Score);
        var toneAchievement = (careerRules.ToneAchievements ?? [])
            .OrderByDescending(achievement => achievement.Priority)
            .FirstOrDefault(achievement => achievement.TerritoryFateIds.Contains(outcome.TerritoryFateId)
                && (achievement.FinalChoiceId is null || achievement.FinalChoiceId == choice.ChoiceId)
                && achievement.MinimumAffinityScores.All(requirement =>
                    affinityScoreById.GetValueOrDefault(requirement.Key) >= requirement.Value));
        var endingTone = toneAchievement?.EndingTone ?? baseTone;
        var careerAffinityId = intendedSucceeded && !string.IsNullOrWhiteSpace(intended.CareerAffinityId)
            ? intended.CareerAffinityId
            : topAffinities.FirstOrDefault()?.AffinityId;
        if (careerAffinityId is null || !careerRules.Templates.TryGetValue(careerAffinityId, out var occupationTemplate))
            throw new InvalidOperationException($"Career template was not found: {careerAffinityId}");
        if (!occupationTemplate.Occupations.TryGetValue(careerRank, out var rankedOccupationName))
            throw new InvalidOperationException($"Occupation template was not found: {careerAffinityId}/{careerRank}");
        var occupationName = toneAchievement?.OccupationName ?? rankedOccupationName;
        var affinityFacts = topAffinities.Select(affinity =>
        {
            var template = careerRules.Templates[affinity.AffinityId];
            return new CareerAffinityResponse(affinity.AffinityId, affinity.Score, template.Evidence);
        }).ToArray();
        var careerEvidence = new[] { choice.ResultNarration }
            .Concat(toneAchievement is null ? [] : new[] { toneAchievement.Evidence })
            .Concat(affinityFacts.Select(fact => fact.Evidence))
            .Distinct()
            .Take(3)
            .ToArray();
        var timingRecord = timedOut
            ? "결정 시간이 끝나 주민 대피 명령이 자동으로 내려졌다."
            : choice.ResultNarration;
        var endingRecords = new List<string>
        {
            timingRecord,
            $"최종 준비 상태: 군사 {state.Military}, 재정 {state.Treasury}, 정통성 {state.Legitimacy}, 봉인 {state.Ward}",
            $"준비 기준을 충족한 핵심 상태는 {preparedCount}개였다."
        };
        if (toneAchievement is not null) endingRecords.Add($"해금 업적: {toneAchievement.DisplayName}");
        return new EndingResponse(occupationName, outcome.Title, outcome.TerritoryOutcome,
            endingRecords, outcome.Traits, outcome.ShareLine,
            outcome.TerritoryFateId, careerRank, endingTone, affinityFacts, careerEvidence);
    }

    private static int GetStateValue(GameStateResponse state, string id) => id switch
    {
        "military" => state.Military,
        "treasury" => state.Treasury,
        "legitimacy" => state.Legitimacy,
        "ward" => state.Ward,
        _ => throw new InvalidOperationException($"Unknown final requirement: {id}")
    };

    private static IReadOnlyDictionary<string, int> MergeChanges(params IReadOnlyDictionary<string, int>[] sources)
    {
        var result = new Dictionary<string, int>();
        foreach (var source in sources)
        foreach (var (key, value) in source)
            result[key] = result.GetValueOrDefault(key) + value;
        return result;
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

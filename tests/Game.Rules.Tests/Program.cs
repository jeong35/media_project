using Game.Api.Contracts;
using Game.Api.Data;
using Game.Api.Errors;
using Game.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;

var scenarios = new[]
{
    new FateScenario("자력 생존-기사단", "rebuild_knights", "train_defenders", "farm_with_residents",
        "community", "manual", "final_defense", false, "self_saved", "legendary", "prestige", "martial", "구국의 기사단장"),
    new FateScenario("자력 생존-봉인식", "prepare_evacuation", "research_ward", "farm_with_residents",
        "community", "manual", "final_ward", false, "self_saved", "accomplished", "orthodox", "leadership", "북부 결계술사"),
    new FateScenario("외부 지원", "prepare_evacuation", "seek_allies", "improve_farm_tools",
        "market", "tools", "final_external_aid", false, "external_aid", "accomplished", "unexpected", "diplomacy", "왕국 공인 농기구 외교관"),
    new FateScenario("전설 위상-봉인식", "prepare_evacuation", "research_ward", "test_magic_irrigation",
        "ward", "magic", "final_ward", false, "self_saved", "legendary", "prestige", "arcane", "대결계의 마탑주"),
    new FateScenario("B급 톤-마력 농기구", "prepare_evacuation", "research_ward", "improve_farm_tools",
        "ward", "tools", "final_ward", false, "self_saved", "accomplished", "b_grade", "arcane", "균열 봉합 농기구 명장"),
    new FateScenario("피해 생존", "prepare_evacuation", "research_ward", "farm_with_residents",
        "community", "manual", "final_defense", false, "damaged_survival", "survivor", "orthodox", "leadership", "피난민의 길잡이"),
    new FateScenario("시간 초과 대피", "repair_beacon", "research_ward", "test_magic_irrigation",
        "ward", "magic", "final_evacuate", true, "people_evacuated", "survivor", "orthodox", "arcane", "피난민의 길잡이"),
    new FateScenario("영지 멸망", "repair_beacon", "research_ward", "test_magic_irrigation",
        "ward", "magic", "final_defense", false, "destroyed", "fallen", "orthodox", "arcane", "유랑 결계 연구자")
};

var content = MockContentLoader.Load();
var results = new List<TestResult>();
foreach (var scenario in scenarios)
    results.Add(await RunFateScenarioAsync(content, scenario));
results.Add(await RunTimeoutGuardAsync(content));
results.Add(await RunIdempotencyAsync(content));
results.Add(await RunDecisionBridgeGuardAsync(content));
results.Add(await RunStoryBeatContractAsync(content));
results.Add(RunLegacyEndingCompatibility());
results.Add(await RunDialogueFailureScenarioAsync(content, "transient_once", false, "mock"));
results.Add(await RunDialogueFailureScenarioAsync(content, "invalid_payload", true, "fixed_fallback"));
results.Add(await RunDialogueFailureScenarioAsync(content, "timeout", true, "fixed_fallback"));
results.Add(await RunLiveProviderScenarioAsync(content, new StubDialogueProvider("openai", StubProviderBehavior.Success),
    "openai", false, 1));
results.Add(await RunLiveProviderScenarioAsync(content, new StubDialogueProvider("openai", StubProviderBehavior.TransientThenSuccess),
    "openai", false, 2));
results.Add(await RunLiveProviderScenarioAsync(content, new StubDialogueProvider("openai", StubProviderBehavior.PermanentFailure),
    "fixed_fallback", true, 1));
results.Add(await RunLiveProviderScenarioAsync(content, new StubDialogueProvider("openai", StubProviderBehavior.NullCareerFeedback),
    "openai", false, 1));
results.Add(RunPersonaPromptBoundary());
results.Add(RunLiveApiMetricsAggregation());
results.Add(await RunOpenAiHttpContractAsync(content));
results.Add(await RunDialogueContextContinuityAsync(content));
results.Add(await RunOpenAiQuotaFallbackAsync(content));
results.Add(await RunLiveCareerEndingScenarioAsync(content, CareerStubBehavior.Success,
    "openai", false, "북부의 구국의 기사단장"));
results.Add(await RunLiveCareerEndingScenarioAsync(content, CareerStubBehavior.InvalidOccupation,
    "fixed_fallback", true, "구국의 기사단장"));
results.Add(await RunMockCareerCandidateSelectionAsync());
results.Add(await RunOpenAiCareerEndingHttpContractAsync(content));
results.Add(await RunFarmingProgressRecoveryAsync(content));
results.Add(await RunFinalTimerRecoveryAsync(content));
results.Add(await RunDialogueHistoryContractAsync(content));
results.Add(await RunDialogueHistoryActIsolationAsync(content));
results.Add(await RunDialogueDiagnosticsContractAsync(content));
results.Add(await RunDialogueHistoryErrorsAsync(content));

Console.WriteLine("| 테스트 | 기대 결과 | 실제 결과 | 최종 상태 | 판정 |");
Console.WriteLine("| --- | --- | --- | --- | --- |");
foreach (var result in results)
    Console.WriteLine($"| {result.Name} | {result.Expected} | {result.Actual} | {result.State} | {(result.Passed ? "PASS" : "FAIL")} |");

var failures = results.Where(result => !result.Passed).ToArray();
if (failures.Length == 0)
{
    Console.WriteLine($"\nALL_RULE_TESTS_PASSED: {results.Count}/{results.Count}");
    return 0;
}

Console.Error.WriteLine($"\nRULE_TESTS_FAILED: {failures.Length}/{results.Count}");
foreach (var failure in failures) Console.Error.WriteLine($"- {failure.Name}: {failure.Error}");
return 1;

static async Task<TestResult> RunFateScenarioAsync(MockGameContent content, FateScenario scenario)
{
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var progress = await AdvanceToFinalChoiceAsync(fixture.Service, scenario);
        var result = await fixture.Service.SubmitChoiceAsync(progress.SessionId,
            new ChoiceRequest(scenario.FinalChoice, NewKey(), scenario.TimedOut));
        var ending = result.Ending ?? throw new InvalidOperationException("엔딩 응답이 없습니다.");
        var actual = ending.TerritoryFateId;
        var snapshot = await fixture.Service.GetSessionAsync(progress.SessionId);
        Assert(snapshot.Status == "Completed", "최종 선택 뒤 세션이 Completed가 아닙니다.");
        Assert(snapshot.Ending?.TerritoryFateId == actual, "저장된 엔딩과 응답 엔딩이 다릅니다.");
        Assert(snapshot.Ending?.CareerRank == ending.CareerRank, "저장된 엔딩의 직업 위상이 다릅니다.");
        var dominantAffinity = ending.TopCareerAffinities?.FirstOrDefault()?.AffinityId ?? "missing_affinity";
        var passed = actual == scenario.ExpectedFate && ending.CareerRank == scenario.ExpectedRank
            && dominantAffinity == scenario.ExpectedDominantAffinity && ending.OccupationName == scenario.ExpectedOccupation
            && ending.EndingTone == scenario.ExpectedTone && ending.CareerEvidence?.Count >= 2
            && !string.IsNullOrWhiteSpace(ending.Epilogue) && ending.GenerationSource == "mock"
            && !ending.GenerationFallbackUsed;
        var expected = $"{scenario.ExpectedFate}/{scenario.ExpectedRank}/{scenario.ExpectedTone}/{scenario.ExpectedOccupation}";
        var actualSummary = $"{actual}/{ending.CareerRank}/{ending.EndingTone}/{ending.OccupationName}";
        return new TestResult(scenario.Name, expected, actualSummary, $"{FormatState(result.State)}, 1순위 {dominantAffinity}",
            passed, passed ? null : "영지 운명, 직업 위상, 적성 또는 직업명이 기대와 다릅니다.");
    }
    catch (Exception error)
    {
        return new TestResult(scenario.Name, scenario.ExpectedFate, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunTimeoutGuardAsync(MockGameContent content)
{
    const string expected = "INVALID_TIMEOUT_CHOICE";
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var scenario = new FateScenario("", "repair_beacon", "research_ward", "test_magic_irrigation",
            "ward", "magic", "final_defense", true, expected, "", "", "", "");
        var progress = await AdvanceToFinalChoiceAsync(fixture.Service, scenario);
        try
        {
            await fixture.Service.SubmitChoiceAsync(progress.SessionId,
                new ChoiceRequest("final_defense", NewKey(), true));
            return new TestResult("시간 초과 선택 검증", expected, "accepted", FormatState(progress.State), false,
                "시간 초과인데 대피 외 선택을 허용했습니다.");
        }
        catch (GameRuleException error)
        {
            return new TestResult("시간 초과 선택 검증", expected, error.Code, FormatState(progress.State),
                error.Code == expected, error.Code == expected ? null : error.Message);
        }
    }
    catch (Exception error)
    {
        return new TestResult("시간 초과 선택 검증", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunIdempotencyAsync(MockGameContent content)
{
    const string expected = "군사 40, 재정 20";
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var start = await fixture.Service.StartSessionAsync("mock");
        var dialogueKey = NewKey();
        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "기사단을 정비한다.", dialogueKey));
        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "기사단을 정비한다.", dialogueKey));
        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "비용과 위험도도 비교한다.", NewKey()));
        await fixture.Service.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
        var choiceKey = NewKey();
        await fixture.Service.SubmitChoiceAsync(start.SessionId, new ChoiceRequest("rebuild_knights", choiceKey));
        await fixture.Service.SubmitChoiceAsync(start.SessionId, new ChoiceRequest("rebuild_knights", choiceKey));
        var snapshot = await fixture.Service.GetSessionAsync(start.SessionId);
        var actual = $"군사 {snapshot.State.Military}, 재정 {snapshot.State.Treasury}";
        var eventCount = await fixture.Database.GameEvents.CountAsync(gameEvent => gameEvent.SessionId == start.SessionId);
        var passed = actual == expected && eventCount == 5;
        return new TestResult("중복 요청 방지", expected, actual, $"이벤트 {eventCount}개", passed,
            passed ? null : "세션 시작, 고유 대화 2회, 결정 전환 1회, 선택 1회가 각각 한 번만 기록되어야 합니다.");
    }
    catch (Exception error)
    {
        return new TestResult("중복 요청 방지", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunDecisionBridgeGuardAsync(MockGameContent content)
{
    const string expected = "2턴 전 차단/2턴 후 허용";
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var start = await fixture.Service.StartSessionAsync("mock");
        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "여기가 어디인지부터 알고 싶다.", NewKey()));

        var blocked = false;
        try
        {
            await fixture.Service.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
        }
        catch (GameRuleException error)
        {
            blocked = error.Code == "DIALOGUE_MINIMUM_NOT_REACHED";
        }

        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "그럼 지금 가장 급한 문제는 뭐지?", NewKey()));
        var bridge = await fixture.Service.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
        var snapshot = await fixture.Service.GetSessionAsync(start.SessionId);
        var passed = blocked && bridge.ChoicesAvailable && !bridge.FallbackUsed
            && bridge.ResponseSource == "mock" && snapshot.Phase == "AwaitingChoice";
        return new TestResult("결정 전환 최소 대화 검증", expected,
            passed ? expected : "전환 규칙 불일치", snapshot.Phase, passed,
            passed ? null : "최소 대화 수 또는 전환 상태 검증에 실패했습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("결정 전환 최소 대화 검증", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunStoryBeatContractAsync(MockGameContent content)
{
    const string expected = "프롤로그/균열 전조/7일 후 최종 명령";
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var start = await fixture.Service.StartSessionAsync("mock");
        var act1Beats = start.Scene.StoryBeats ?? [];
        var act4 = content.GetAct(4);
        var act4Beats = act4.StoryBeats ?? [];
        var finaleBeats = act4.FinaleBeats ?? [];
        var passed = act1Beats.Count >= 8
            && act1Beats.Any(beat => beat.BackgroundId == "modern_library")
            && act1Beats.Any(beat => beat.Text.Contains("3년 뒤"))
            && act4Beats.Any(beat => beat.Text.Contains("7일 뒤"))
            && finaleBeats.Any(beat => beat.BackgroundId == "cracked_sky_castle_wall")
            && finaleBeats.Any(beat => beat.Text.Contains("최종 명령"));
        var actual = passed ? expected : $"Act1 {act1Beats.Count}/Act4 {act4Beats.Count}/Finale {finaleBeats.Count}";
        return new TestResult("확정 스토리 비트 계약", expected, actual,
            $"도입 {act1Beats.Count}개/전조 {act4Beats.Count}개/최종 {finaleBeats.Count}개", passed,
            passed ? null : "프롤로그, 균열 전조 또는 최종 명령 장면이 API 콘텐츠에서 누락됐습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("확정 스토리 비트 계약", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static TestResult RunLegacyEndingCompatibility()
{
    const string expected = "survivor/orthodox";
    try
    {
        const string oldEndingJson = """
            {"occupationName":"견습 영주","title":"이전 엔딩","territoryOutcome":"생존","records":[],"traits":[],"shareLine":"기록","territoryFateId":"self_saved"}
            """;
        var ending = JsonSerializer.Deserialize<EndingResponse>(oldEndingJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var actual = $"{ending?.CareerRank}/{ending?.EndingTone}";
        var passed = actual == expected && ending is not null
            && ending.TopCareerAffinities is null && ending.CareerEvidence is null
            && ending.Epilogue == string.Empty && ending.GenerationSource == "fixed_template";
        return new TestResult("기존 엔딩 복구 호환", expected, actual, "신규 필드가 없는 저장 JSON", passed,
            passed ? null : "이전 버전의 완료 세션을 새 엔딩 계약으로 읽지 못했습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("기존 엔딩 복구 호환", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunDialogueFailureScenarioAsync(MockGameContent content, string scenario,
    bool expectedFallback, string expectedSource)
{
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var start = await fixture.Service.StartSessionAsync("mock", scenario);
        var response = await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "실패 상황에서도 계획을 계속한다.", NewKey()));
        var snapshot = await fixture.Service.GetSessionAsync(start.SessionId);
        var passed = response.FallbackUsed == expectedFallback && response.ResponseSource == expectedSource
            && response.AttemptCount == 2 && !response.ChoicesAvailable && response.DialogueRemaining == 4
            && snapshot.Phase == "AwaitingDialogue";
        var expected = $"{expectedSource}/fallback={expectedFallback.ToString().ToLowerInvariant()}/선택 진행";
        var actual = $"{response.ResponseSource}/fallback={response.FallbackUsed.ToString().ToLowerInvariant()}/{snapshot.Phase}";
        return new TestResult($"대화 실패 복구-{scenario}", expected, actual, $"시도 {response.AttemptCount}회", passed,
            passed ? null : "재시도 또는 고정 대사 폴백 뒤 남은 대화 횟수를 유지하지 못했습니다.");
    }
    catch (Exception error)
    {
        return new TestResult($"대화 실패 복구-{scenario}", expectedSource, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunLiveProviderScenarioAsync(MockGameContent content, IDialogueProvider liveProvider,
    string expectedSource, bool expectedFallback, int expectedAttempts)
{
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content, liveProvider);
        var start = await fixture.Service.StartSessionAsync("live");
        var response = await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "주민이 안전한 계획을 세운다.", NewKey()));
        var snapshot = await fixture.Service.GetSessionAsync(start.SessionId);
        var passed = response.ResponseSource == expectedSource && response.FallbackUsed == expectedFallback
            && response.AttemptCount == expectedAttempts && !response.ChoicesAvailable
            && response.DialogueRemaining == 4 && snapshot.Phase == "AwaitingDialogue";
        var expected = $"{expectedSource}/fallback={expectedFallback.ToString().ToLowerInvariant()}/시도 {expectedAttempts}";
        var actual = $"{response.ResponseSource}/fallback={response.FallbackUsed.ToString().ToLowerInvariant()}/시도 {response.AttemptCount}";
        return new TestResult($"Live Provider-{((StubDialogueProvider)liveProvider).Behavior}", expected, actual,
            snapshot.Phase, passed, passed ? null : "Provider 선택, 재시도 또는 폴백 경계가 예상과 다릅니다.");
    }
    catch (Exception error)
    {
        return new TestResult("Live Provider", expectedSource, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunOpenAiHttpContractAsync(MockGameContent content)
{
    const string expected = "Responses API/json_schema/output_text";
    try
    {
        var generated = new GeneratedDialogue("구조화된 대사입니다.", "calm", "응답 근거입니다.",
            ["kind"], "직업 근거입니다.", "대화 요약입니다.");
        var handler = new CapturingHttpHandler((_, _) => JsonResponse(HttpStatusCode.OK, new
        {
            status = "completed",
            output = new[] { new { type = "message", content = new[]
                { new { type = "output_text", text = JsonSerializer.Serialize(generated, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } } } }
        }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var metrics = new LiveApiMetrics();
        var provider = new OpenAiDialogueProvider(client, Options.Create(new OpenAiOptions
        {
            ApiKey = "test-placeholder", Model = "test-model", TimeoutSeconds = 2, MaxOutputTokens = 300
        }), metrics);
        var personaJson = NpcPersonaPromptBuilder.Build(NpcPersonaCatalog.Load().Get("steward_edwin"));
        var context = new DialogueConversationContext("주민 안전을 먼저 검토하기로 했다.",
            "비용도 함께 알려 달라.", "세 가지 방안의 비용을 정리하겠습니다.");
        var raw = await provider.GenerateAsync(new DialogueProviderRequest(content.GetAct(1), "주민을 먼저 지킨다.",
            "none", 1, ["kind", "pragmatic"], personaJson, context));
        var parsed = JsonSerializer.Deserialize<GeneratedDialogue>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var requestDocument = JsonDocument.Parse(handler.RequestBody!);
        var prompt = requestDocument.RootElement.GetProperty("input")[1].GetProperty("content").GetString()!;
        var requestOk = handler.RequestBody?.Contains("\"json_schema\"") == true
            && handler.RequestBody.Contains("\"additionalProperties\":false")
            && handler.RequestBody.Contains("\"maxLength\":500")
            && handler.RequestBody.Contains("\"maxLength\":300")
            && handler.RequestBody.Contains("\"minLength\":1")
            && handler.RequestBody.Contains("\"type\":[\"string\",\"null\"]")
            && prompt.Contains("core_traits") && prompt.Contains("결론부터 말한다")
            && prompt.Contains(context.Summary) && prompt.Contains(context.PreviousPlayerText)
            && prompt.Contains(context.PreviousNpcDialogue) && prompt.Contains("현재 플레이어 발화")
            && !prompt.Contains("hidden_traits") && !prompt.Contains("backstory_elements")
            && !prompt.Contains("career_influence_weights") && !prompt.Contains("disclosure_policy")
            && handler.AuthorizationScheme == "Bearer" && handler.RequestUri?.AbsolutePath == "/v1/responses";
        var metric = metrics.Snapshot().Dialogue;
        var passed = handler.CallCount == 1 && requestOk && parsed?.Dialogue == generated.Dialogue
            && metric.NetworkCallCount == 1 && metric.SuccessfulCallCount == 1
            && metric.FailedCallCount == 0 && metric.SuccessRatePercent == 100
            && metric.AverageLatencyMs is >= 0 && metric.MaximumLatencyMs is >= 0
            && metric.FallbackCount == 0;
        return new TestResult("OpenAI HTTP 계약", expected,
            passed ? expected : "request-or-response-mismatch",
            $"HTTP 호출 {handler.CallCount}회/측정 성공 {metric.SuccessfulCallCount}회", passed,
            passed ? null : "Responses API 요청 스키마 또는 output_text 추출이 계약과 다릅니다.");
    }
    catch (Exception error)
    {
        return new TestResult("OpenAI HTTP 계약", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunDialogueContextContinuityAsync(MockGameContent content)
{
    const string expected = "2턴에 누적 요약/직전 플레이어 발화/직전 NPC 답변 전달";
    try
    {
        var provider = new StubDialogueProvider("openai", StubProviderBehavior.Success);
        await using var fixture = await GameFixture.CreateAsync(content, provider);
        var start = await fixture.Service.StartSessionAsync("live");
        const string firstPlayerText = "주민 안전과 비용을 함께 검토해 줘.";
        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, firstPlayerText, NewKey()));
        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "그 내용을 바탕으로 우선순위를 말해 줘.", NewKey()));

        var secondContext = provider.Requests.ElementAtOrDefault(1)?.ConversationContext;
        var latestEvent = await fixture.Database.GameEvents
            .Where(gameEvent => gameEvent.SessionId == start.SessionId && gameEvent.EventType == "DialogueGenerated")
            .OrderByDescending(gameEvent => gameEvent.Sequence)
            .FirstAsync();
        using var eventDocument = JsonDocument.Parse(latestEvent.PayloadJson);
        var eventStoresContext = eventDocument.RootElement.TryGetProperty("playerText", out _)
            && eventDocument.RootElement.TryGetProperty("conversationSummary", out _);
        var passed = provider.Requests.Count == 2
            && secondContext?.Summary == "Live 응답 요약"
            && secondContext.PreviousPlayerText == firstPlayerText
            && secondContext.PreviousNpcDialogue == "고정 계약을 지킨 Live 대사입니다."
            && eventStoresContext;
        return new TestResult("대화 문맥 연속성", expected, passed ? expected : "context-mismatch",
            $"Provider 요청 {provider.Requests.Count}회/이벤트 문맥 저장 {eventStoresContext}", passed,
            passed ? null : "두 번째 대화 요청에 저장된 직전 턴 문맥이 전달되지 않았습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("대화 문맥 연속성", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static TestResult RunLiveApiMetricsAggregation()
{
    const string expected = "호출 2/성공 1/실패 1/성공률 50%/평균 200ms/최대 300ms/fallback 1";
    try
    {
        var metrics = new LiveApiMetrics();
        metrics.RecordProviderRequest(LiveApiOperation.Dialogue);
        metrics.RecordNetworkCall(LiveApiOperation.Dialogue, TimeSpan.FromMilliseconds(100), true);
        metrics.RecordNetworkCall(LiveApiOperation.Dialogue, TimeSpan.FromMilliseconds(300), false);
        metrics.RecordFallback(LiveApiOperation.Dialogue);
        var snapshot = metrics.Snapshot();
        var dialogue = snapshot.Dialogue;
        var career = snapshot.CareerEnding;
        var passed = dialogue.ProviderRequestCount == 1 && dialogue.NetworkCallCount == 2
            && dialogue.SuccessfulCallCount == 1 && dialogue.FailedCallCount == 1
            && dialogue.SuccessRatePercent == 50 && dialogue.AverageLatencyMs == 200
            && dialogue.MaximumLatencyMs == 300 && dialogue.FallbackCount == 1
            && snapshot.Overall.NetworkCallCount == 2 && snapshot.Overall.SuccessRatePercent == 50
            && career.NetworkCallCount == 0 && career.SuccessRatePercent is null
            && career.AverageLatencyMs is null && career.MaximumLatencyMs is null;
        var actual = $"호출 {dialogue.NetworkCallCount}/성공 {dialogue.SuccessfulCallCount}/실패 {dialogue.FailedCallCount}"
            + $"/성공률 {dialogue.SuccessRatePercent}%/평균 {dialogue.AverageLatencyMs}ms"
            + $"/최대 {dialogue.MaximumLatencyMs}ms/fallback {dialogue.FallbackCount}";
        return new TestResult("Live API 측정치 집계", expected, actual, "전체/대화/직업 분리", passed,
            passed ? null : "호출·성공·실패·지연·폴백 집계가 예상과 다릅니다.");
    }
    catch (Exception error)
    {
        return new TestResult("Live API 측정치 집계", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static TestResult RunPersonaPromptBoundary()
{
    const string expected = "공개 필드 포함/숨김·판정 필드 제외";
    try
    {
        var persona = NpcPersonaCatalog.Load().Get("steward_edwin");
        var prompt = NpcPersonaPromptBuilder.Build(persona);
        var rawPath = Path.Combine(AppContext.BaseDirectory, "data", "npc", "Edwin.json");
        var raw = File.ReadAllText(rawPath);
        var publicFieldsIncluded = prompt.Contains("core_traits") && prompt.Contains("speech_style")
            && prompt.Contains("behavior_rules") && prompt.Contains("결론부터 말한다");
        var privateFieldsExistInSource = raw.Contains("hidden_traits") && raw.Contains("backstory_elements");
        var privateFieldsExcluded = !prompt.Contains("hidden_traits") && !prompt.Contains("backstory_elements")
            && !prompt.Contains("career_influence_weights") && !prompt.Contains("career_influence_rules")
            && !prompt.Contains("disclosure_policy") && !prompt.Contains("game_function");
        var passed = persona.DisclosurePolicy == "progressive_relationship" && publicFieldsIncluded
            && privateFieldsExistInSource && privateFieldsExcluded;
        return new TestResult("NPC 공개 페르소나 경계", expected, passed ? expected : "필드 경계 불일치",
            persona.Id, passed, passed ? null : "대화 프롬프트가 공개 필드만 포함하지 않습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("NPC 공개 페르소나 경계", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunOpenAiQuotaFallbackAsync(MockGameContent content)
{
    const string expected = "fixed_fallback/시도 1";
    try
    {
        var handler = new CapturingHttpHandler((_, _) => JsonResponse(HttpStatusCode.TooManyRequests,
            new { error = new { code = "credit_balance_exhausted", message = "quota" } }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var metrics = new LiveApiMetrics();
        var provider = new OpenAiDialogueProvider(client, Options.Create(new OpenAiOptions
        {
            ApiKey = "test-placeholder", Model = "test-model", TimeoutSeconds = 2
        }), metrics);
        await using var fixture = await GameFixture.CreateAsync(content, provider, liveApiMetrics: metrics);
        var start = await fixture.Service.StartSessionAsync("live");
        var response = await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "계획을 계속한다.", NewKey()));
        var actual = $"{response.ResponseSource}/시도 {response.AttemptCount}";
        var metric = metrics.Snapshot().Dialogue;
        var passed = actual == expected && response.FallbackUsed && handler.CallCount == 1
            && metric.ProviderRequestCount == 1 && metric.NetworkCallCount == 1
            && metric.SuccessfulCallCount == 0 && metric.FailedCallCount == 1
            && metric.SuccessRatePercent == 0 && metric.FallbackCount == 1;
        return new TestResult("OpenAI 영구 한도 오류", expected, actual,
            $"HTTP 호출 {handler.CallCount}회/측정 실패 {metric.FailedCallCount}회/fallback {metric.FallbackCount}회", passed,
            passed ? null : "결제·사용량 한도 오류를 불필요하게 재시도했습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("OpenAI 영구 한도 오류", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunLiveCareerEndingScenarioAsync(MockGameContent content,
    CareerStubBehavior behavior, string expectedSource, bool expectedFallback, string expectedOccupation)
{
    try
    {
        var dialogueProvider = new StubDialogueProvider("openai", StubProviderBehavior.Success);
        var endingProvider = new StubCareerEndingProvider("openai", behavior);
        await using var fixture = await GameFixture.CreateAsync(content, dialogueProvider, endingProvider);
        var scenario = new FateScenario("Live 직업 후일담", "rebuild_knights", "train_defenders",
            "farm_with_residents", "community", "manual", "final_defense", false,
            "self_saved", "legendary", "prestige", "martial", "구국의 기사단장");
        var progress = await AdvanceToFinalChoiceAsync(fixture.Service, scenario, "live");
        var result = await fixture.Service.SubmitChoiceAsync(progress.SessionId,
            new ChoiceRequest(scenario.FinalChoice, NewKey(), false));
        var ending = result.Ending ?? throw new InvalidOperationException("엔딩 응답이 없습니다.");
        var passed = ending.TerritoryFateId == "self_saved" && ending.CareerRank == "legendary"
            && ending.EndingTone == "prestige" && ending.OccupationName == expectedOccupation
            && ending.GenerationSource == expectedSource && ending.GenerationFallbackUsed == expectedFallback
            && ending.GenerationAttemptCount == 1 && !string.IsNullOrWhiteSpace(ending.Epilogue)
            && (expectedFallback
                ? ending.GenerationCandidateCount == 0 && ending.SelectedCandidateIndex == -1
                : ending.GenerationCandidateCount == 3 && ending.SelectedCandidateIndex == 1 && ending.SelectionScore > 0);
        var expected = $"self_saved/legendary/prestige/{expectedOccupation}/{expectedSource}";
        var actual = $"{ending.TerritoryFateId}/{ending.CareerRank}/{ending.EndingTone}/{ending.OccupationName}/{ending.GenerationSource}";
        return new TestResult($"Live 직업 후일담-{behavior}", expected, actual,
            $"fallback={ending.GenerationFallbackUsed.ToString().ToLowerInvariant()}", passed,
            passed ? null : "생성 표현이 서버 판정값을 보존하지 못했거나 폴백 경계가 다릅니다.");
    }
    catch (Exception error)
    {
        return new TestResult($"Live 직업 후일담-{behavior}", expectedSource, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunMockCareerCandidateSelectionAsync()
{
    const string expected = "후보 3개/2번 후보 선택/근거 2개 반영";
    try
    {
        var facts = new CareerEndingFacts("구국의 기사단장", "칭호", "스스로 살아남은 영지", "self_saved",
            "legendary", "prestige", [new CareerAffinityResponse("martial", 30, "전투 지휘")],
            ["기사단을 이끌어 균열을 막았다.", "주민 대피로를 끝까지 지켰다."], ["용기", "지도력"]);
        var service = new CareerEndingGenerationService([new MockCareerEndingProvider()], new LiveApiMetrics());
        var generated = await service.GenerateAsync("mock", facts);
        var evidenceCount = facts.CareerEvidence.Count(evidence =>
            generated.Content.Epilogue.Contains(evidence.TrimEnd('.'), StringComparison.Ordinal));
        var passed = generated.CandidateCount == 3 && generated.SelectedCandidateIndex == 1
            && evidenceCount == 2 && generated.SelectionScore > 0 && !generated.FallbackUsed;
        var actual = $"후보 {generated.CandidateCount}개/{generated.SelectedCandidateIndex + 1}번 후보 선택/근거 {evidenceCount}개 반영";
        return new TestResult("Mock 직업 후보 점수 선택", expected, actual,
            $"점수 {generated.SelectionScore}", passed,
            passed ? null : "서버가 세 후보 중 플레이 근거가 가장 풍부한 후보를 고르지 못했습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("Mock 직업 후보 점수 선택", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunOpenAiCareerEndingHttpContractAsync(MockGameContent content)
{
    const string expected = "Responses API/ending_json_schema/output_text";
    try
    {
        var facts = new CareerEndingFacts("구국의 기사단장", "칭호", "스스로 살아남은 영지", "self_saved",
            "legendary", "prestige", [new CareerAffinityResponse("martial", 30, "전투 지휘")],
            ["기사단을 이끌어 균열을 막았다."], ["용기", "지도력"]);
        var generated = new GeneratedCareerEndingCandidates([
            new GeneratedCareerEnding("북부의 구국의 기사단장",
                "영지는 살아남았다. 그는 기사단을 이끌었다. 사람들은 그를 오래 기억했다."),
            new GeneratedCareerEnding("구국의 기사단장",
                "영지는 위기를 넘겼다. 그는 주민을 지켰다. 북부는 그를 오래 기억했다."),
            new GeneratedCareerEnding("왕국의 구국의 기사단장",
                "균열은 닫혔다. 그는 마지막까지 싸웠다. 기록은 다음 세대에 전해졌다.")
        ]);
        var handler = new CapturingHttpHandler((_, _) => JsonResponse(HttpStatusCode.OK, new
        {
            status = "completed",
            output = new[] { new { type = "message", content = new[]
                { new { type = "output_text", text = JsonSerializer.Serialize(generated, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } } } }
        }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var metrics = new LiveApiMetrics();
        var provider = new OpenAiCareerEndingProvider(client, Options.Create(new OpenAiOptions
        {
            ApiKey = "test-placeholder", Model = "test-model", TimeoutSeconds = 2, MaxOutputTokens = 300
        }), metrics);
        var raw = await provider.GenerateAsync(new CareerEndingProviderRequest(facts, 1));
        var parsed = JsonSerializer.Deserialize<GeneratedCareerEndingCandidates>(raw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var requestDocument = JsonDocument.Parse(handler.RequestBody!);
        var factsJson = requestDocument.RootElement.GetProperty("input")[1].GetProperty("content").GetString()!;
        using var factsDocument = JsonDocument.Parse(factsJson);
        var factsOk = factsDocument.RootElement.GetProperty("careerRank").GetString() == "legendary"
            && factsDocument.RootElement.GetProperty("endingTone").GetString() == "prestige"
            && factsDocument.RootElement.GetProperty("templateOccupationName").GetString() == "구국의 기사단장";
        var requestOk = handler.RequestBody?.Contains("\"career_ending_expression\"") == true
            && handler.RequestBody.Contains("\"additionalProperties\":false")
            && handler.RequestBody.Contains("\"minItems\":3")
            && handler.RequestBody.Contains("\"maxItems\":3")
            && factsOk
            && !handler.RequestBody.Contains("maxLength")
            && handler.AuthorizationScheme == "Bearer" && handler.RequestUri?.AbsolutePath == "/v1/responses";
        var metric = metrics.Snapshot().CareerEnding;
        var passed = handler.CallCount == 1 && requestOk && parsed?.Candidates.Count == 3
            && parsed.Candidates[0].OccupationName == generated.Candidates[0].OccupationName
            && metric.NetworkCallCount == 1 && metric.SuccessfulCallCount == 1
            && metric.FailedCallCount == 0 && metric.SuccessRatePercent == 100
            && metric.AverageLatencyMs is >= 0 && metric.MaximumLatencyMs is >= 0
            && metric.FallbackCount == 0;
        return new TestResult("OpenAI 직업 후일담 HTTP 계약", expected,
            passed ? expected : "request-or-response-mismatch",
            $"HTTP 호출 {handler.CallCount}회/측정 성공 {metric.SuccessfulCallCount}회", passed,
            passed ? null : "후일담 Responses API 요청 스키마 또는 output_text 추출이 계약과 다릅니다.");
    }
    catch (Exception error)
    {
        return new TestResult("OpenAI 직업 후일담 HTTP 계약", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object payload) => new(statusCode)
{
    Content = new StringContent(JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        Encoding.UTF8, "application/json")
};

static async Task<TestResult> RunFarmingProgressRecoveryAsync(MockGameContent content)
{
    const string expected = "장애물 0,2/씨앗 0,3/market/tools";
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var sessionId = await AdvanceToFarmingAsync(fixture.Service);
        var key = NewKey();
        await fixture.Service.SaveFarmingProgressAsync(sessionId,
            new FarmingProgressRequest([0, 2], [0, 3], "market", "tools", key));
        await fixture.Service.SaveFarmingProgressAsync(sessionId,
            new FarmingProgressRequest([0, 2], [0, 3], "market", "tools", key));
        fixture.Database.ChangeTracker.Clear();

        var snapshot = await fixture.Service.GetSessionAsync(sessionId);
        var progress = snapshot.FarmingProgress;
        var actual = progress is null ? "missing" :
            $"장애물 {string.Join(',', progress.RemovedObstacleIndexes)}/씨앗 {string.Join(',', progress.PlantedSeedIndexes)}/{progress.WaterTarget}/{progress.Method}";
        var checkpointCount = await fixture.Database.GameEvents.CountAsync(gameEvent =>
            gameEvent.SessionId == sessionId && gameEvent.EventType == "FarmingProgressSaved");
        var passed = actual == expected && snapshot.Phase == "AwaitingFarming" && checkpointCount == 1;
        return new TestResult("농사 중간 복구", expected, actual, $"체크포인트 {checkpointCount}개", passed,
            passed ? null : "농사 진행 체크포인트가 정확히 복구되지 않았습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("농사 중간 복구", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunFinalTimerRecoveryAsync(MockGameContent content)
{
    const string expected = "남은 시간 유지/만료 시 people_evacuated";
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var scenario = new FateScenario("", "prepare_evacuation", "research_ward", "farm_with_residents",
            "community", "manual", "final_ward", false, "", "", "", "", "");
        var progress = await AdvanceToFinalChoiceAsync(fixture.Service, scenario);
        var recoveredChoices = await fixture.Service.GetChoicesAsync(progress.SessionId);
        var timerPersisted = recoveredChoices.RemainingSeconds is > 0 and < 20;

        var timerEvent = await fixture.Database.GameEvents.SingleAsync(gameEvent =>
            gameEvent.SessionId == progress.SessionId && gameEvent.EventType == "FinalChoiceTimerStarted");
        timerEvent.PayloadJson = JsonSerializer.Serialize(new { deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(-1) },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await fixture.Database.SaveChangesAsync();
        fixture.Database.ChangeTracker.Clear();

        var result = await fixture.Service.SubmitChoiceAsync(progress.SessionId,
            new ChoiceRequest("final_ward", NewKey(), false));
        var actual = $"남은 시간 {(timerPersisted ? "유지" : "초기화")}/만료 시 {result.Ending?.TerritoryFateId}";
        var passed = timerPersisted && result.Ending?.TerritoryFateId == "people_evacuated"
            && result.Ending.Records.First().Contains("자동으로");
        return new TestResult("Act 4 타이머 복구", expected, actual,
            $"복구 잔여 {recoveredChoices.RemainingSeconds:F2}초", passed,
            passed ? null : "제한시간이 초기화되거나 만료 후 다른 선택이 적용됐습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("Act 4 타이머 복구", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunDialogueHistoryContractAsync(MockGameContent content)
{
    const string expected = "opening + 2턴/sequence 정렬/공개 필드만";
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var start = await fixture.Service.StartSessionAsync("mock");
        var openingOnly = await fixture.Service.GetDialogueHistoryAsync(start.SessionId);
        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "첫 번째 질문", NewKey()));
        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "두 번째 질문", NewKey()));
        var history = await fixture.Service.GetDialogueHistoryAsync(start.SessionId);
        var serialized = JsonSerializer.Serialize(history, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var sequences = history.Records.Select(record => record.Sequence).ToArray();
        var passed = openingOnly.Records.Count == 1 && openingOnly.Records[0].IsOpening
            && string.IsNullOrEmpty(openingOnly.Records[0].PlayerText)
            && history.Act == 1 && history.Records.Count == 3
            && history.Records[0].IsOpening && history.Records[0].NpcDialogue == start.OpeningDialogue
            && history.Records[1].PlayerText == "첫 번째 질문"
            && history.Records[2].PlayerText == "두 번째 질문"
            && sequences.SequenceEqual(sequences.Order())
            && !serialized.Contains("feedback", StringComparison.OrdinalIgnoreCase)
            && !serialized.Contains("careerFeedback", StringComparison.OrdinalIgnoreCase)
            && !serialized.Contains("acceptedTags", StringComparison.OrdinalIgnoreCase)
            && !serialized.Contains("conversationSummary", StringComparison.OrdinalIgnoreCase);
        return new TestResult("현재 ACT 대화 기록 공개 계약", expected,
            $"기록 {history.Records.Count}개/sequence {string.Join(',', sequences)}", $"ACT {history.Act}", passed,
            passed ? null : "opening 또는 완료 턴 정렬, 공개 필드 경계가 올바르지 않습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("현재 ACT 대화 기록 공개 계약", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunDialogueHistoryActIsolationAsync(MockGameContent content)
{
    const string expected = "Act 2 opening만 반환/Act 1 제외";
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var start = await fixture.Service.StartSessionAsync("mock");
        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "Act 1 첫 대화", NewKey()));
        await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "Act 1 두 번째 대화", NewKey()));
        await fixture.Service.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
        var result = await fixture.Service.SubmitChoiceAsync(start.SessionId,
            new ChoiceRequest("prepare_evacuation", NewKey()));
        var history = await fixture.Service.GetDialogueHistoryAsync(start.SessionId);
        var passed = result.NextAct?.Act == 2 && history.Act == 2 && history.Records.Count == 1
            && history.Records[0].IsOpening && history.Records[0].Act == 2
            && history.Records.All(record => !record.PlayerText.Contains("Act 1"));
        return new TestResult("ACT 변경 후 기록 격리", expected,
            $"ACT {history.Act}/기록 {history.Records.Count}개", result.NextAct?.Scene.NpcName ?? "-", passed,
            passed ? null : "이전 ACT의 대화가 플레이어용 기록에 섞였습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("ACT 변경 후 기록 격리", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunDialogueDiagnosticsContractAsync(MockGameContent content)
{
    const string expected = "GameEvent 내부 판정 보존/diagnostics 조회";
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var start = await fixture.Service.StartSessionAsync("mock");
        var response = await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "주민을 먼저 지키자", NewKey()));
        var storedEvent = await fixture.Database.GameEvents.SingleAsync(gameEvent =>
            gameEvent.SessionId == start.SessionId && gameEvent.EventType == "DialogueGenerated");
        using var payload = JsonDocument.Parse(storedEvent.PayloadJson);
        var root = payload.RootElement;
        var diagnostics = await fixture.Service.GetDialogueDiagnosticsAsync(start.SessionId, 1);
        var item = diagnostics.Events.Single();
        var stored = root.GetProperty("feedback").GetString() == response.Feedback
            && root.GetProperty("careerFeedback").GetString() == response.CareerFeedback
            && root.TryGetProperty("conversationSummary", out _);
        var passed = stored && item.Feedback == response.Feedback
            && item.CareerFeedback == response.CareerFeedback
            && item.AcceptedTags.SequenceEqual(response.AcceptedTags)
            && item.PlayerText == "주민을 먼저 지키자"
            && item.NpcDialogue == response.Dialogue && item.AttemptCount == response.AttemptCount;
        return new TestResult("Development 대화 진단 계약", expected,
            $"이벤트 {diagnostics.Events.Count}개/태그 {item.AcceptedTags.Count}개", item.ResponseSource, passed,
            passed ? null : "저장 이벤트 또는 진단 응답에서 내부 판정 필드가 누락됐습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("Development 대화 진단 계약", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<TestResult> RunDialogueHistoryErrorsAsync(MockGameContent content)
{
    const string expected = "SESSION_NOT_FOUND/DIALOGUE_HISTORY_UNAVAILABLE";
    try
    {
        await using var fixture = await GameFixture.CreateAsync(content);
        var missingBlocked = false;
        try
        {
            await fixture.Service.GetDialogueHistoryAsync(Guid.NewGuid());
        }
        catch (SessionNotFoundException)
        {
            missingBlocked = true;
        }

        var start = await fixture.Service.StartSessionAsync("mock");
        var session = await fixture.Database.Sessions.SingleAsync(candidate => candidate.Id == start.SessionId);
        session.Phase = "InvalidPhase";
        await fixture.Database.SaveChangesAsync();
        var invalidBlocked = false;
        try
        {
            await fixture.Service.GetDialogueHistoryAsync(start.SessionId);
        }
        catch (GameRuleException error)
        {
            invalidBlocked = error.Code == "DIALOGUE_HISTORY_UNAVAILABLE";
        }

        var passed = missingBlocked && invalidBlocked;
        return new TestResult("대화 기록 오류 처리", expected,
            $"missing={missingBlocked}/invalid={invalidBlocked}", "차단", passed,
            passed ? null : "존재하지 않는 세션 또는 잘못된 상태가 차단되지 않았습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("대화 기록 오류 처리", expected, error.GetType().Name, "-", false, error.Message);
    }
}

static async Task<Guid> AdvanceToFarmingAsync(MockGameService game)
{
    var start = await game.StartSessionAsync("mock");
    await game.SubmitDialogueAsync(start.SessionId, new DialogueRequest(start.Scene.NpcId, "현재 상황부터 정리한다.", NewKey()));
    await game.SubmitDialogueAsync(start.SessionId, new DialogueRequest(start.Scene.NpcId, "첫 결정을 내린다.", NewKey()));
    await game.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
    var act1 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest("prepare_evacuation", NewKey()));
    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act1.NextAct!.Scene.NpcId, "확실한 사실과 가설을 나눠 설명해 달라.", NewKey()));
    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act1.NextAct!.Scene.NpcId, "균열 대응책을 정한다.", NewKey()));
    await game.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
    var act2 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest("research_ward", NewKey()));
    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act2.NextAct!.Scene.NpcId, "주민들이 가장 힘든 점을 묻는다.", NewKey()));
    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act2.NextAct!.Scene.NpcId, "주민의 밭을 돕는다.", NewKey()));
    await game.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
    var act3 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest("farm_with_residents", NewKey()));
    Assert(act3.NextActivity?.ActivityId == "farming", "농사 활동으로 전환되지 않았습니다.");
    return start.SessionId;
}

static async Task<FinalChoiceProgress> AdvanceToFinalChoiceAsync(MockGameService game, FateScenario scenario,
    string mode = "mock")
{
    var start = await game.StartSessionAsync(mode);
    await game.SubmitDialogueAsync(start.SessionId, new DialogueRequest(start.Scene.NpcId, "현재 상황부터 정리한다.", NewKey()));
    await game.SubmitDialogueAsync(start.SessionId, new DialogueRequest(start.Scene.NpcId, "첫 결정을 내린다.", NewKey()));
    await game.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
    var act1 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest(scenario.Act1Choice, NewKey()));
    Assert(act1.NextAct?.Act == 2, "Act 2로 전환되지 않았습니다.");

    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act1.NextAct!.Scene.NpcId, "확실한 사실과 가설을 나눠 설명해 달라.", NewKey()));
    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act1.NextAct!.Scene.NpcId, "균열 대응책을 정한다.", NewKey()));
    await game.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
    var act2 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest(scenario.Act2Choice, NewKey()));
    Assert(act2.NextAct?.Act == 3, "Act 3으로 전환되지 않았습니다.");

    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act2.NextAct!.Scene.NpcId, "주민들이 가장 힘든 점을 묻는다.", NewKey()));
    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act2.NextAct!.Scene.NpcId, "주민의 밭을 돕는다.", NewKey()));
    await game.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
    var act3 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest(scenario.Act3Choice, NewKey()));
    Assert(act3.NextActivity?.ActivityId == "farming", "농사 활동으로 전환되지 않았습니다.");

    var farming = await game.SubmitFarmingAsync(start.SessionId,
        new FarmingResultRequest(3, 3, scenario.WaterTarget, scenario.Method, NewKey()));
    Assert(farming.Score == 90, "농사 점수가 90점이 아닙니다.");
    Assert(farming.NextAct?.Act == 4, "Act 4로 전환되지 않았습니다.");

    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(farming.NextAct!.Scene.NpcId, "최종 명령 전에 위험을 확인한다.", NewKey()));
    await game.SubmitDecisionBridgeAsync(start.SessionId, new DecisionBridgeRequest(NewKey()));
    var choices = await game.GetChoicesAsync(start.SessionId);
    Assert(choices.Choices.Count == 4, "Act 4 최종 선택지가 4개가 아닙니다.");
    Assert(choices.TimeLimitSeconds == 20, "Act 4 제한시간이 20초가 아닙니다.");
    Assert(choices.TimeoutChoiceId == "final_evacuate", "시간 초과 자동 선택이 주민 대피가 아닙니다.");
    return new FinalChoiceProgress(start.SessionId, farming.State);
}

static string NewKey() => Guid.NewGuid().ToString();
static string FormatState(GameStateResponse state) =>
    $"군사 {state.Military}, 재정 {state.Treasury}, 정통성 {state.Legitimacy}, 봉인 {state.Ward}";

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed record FateScenario(string Name, string Act1Choice, string Act2Choice, string Act3Choice,
    string WaterTarget, string Method, string FinalChoice, bool TimedOut, string ExpectedFate,
    string ExpectedRank, string ExpectedTone, string ExpectedDominantAffinity, string ExpectedOccupation);
sealed record FinalChoiceProgress(Guid SessionId, GameStateResponse State);
sealed record TestResult(string Name, string Expected, string Actual, string State, bool Passed, string? Error);

sealed class GameFixture : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    public GameDbContext Database { get; }
    public MockGameService Service { get; }

    private GameFixture(SqliteConnection connection, GameDbContext database, MockGameService service)
    {
        this.connection = connection;
        Database = database;
        Service = service;
    }

    public static async Task<GameFixture> CreateAsync(MockGameContent content, IDialogueProvider? liveProvider = null,
        ICareerEndingProvider? liveEndingProvider = null, LiveApiMetrics? liveApiMetrics = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GameDbContext>().UseSqlite(connection).Options;
        var database = new GameDbContext(options);
        await database.Database.EnsureCreatedAsync();
        var providers = new List<IDialogueProvider> { new MockDialogueProvider() };
        if (liveProvider is not null) providers.Add(liveProvider);
        var metrics = liveApiMetrics ?? new LiveApiMetrics();
        var dialogueGeneration = new DialogueGenerationService(providers, NpcPersonaCatalog.Load(), metrics);
        var endingProviders = new List<ICareerEndingProvider> { new MockCareerEndingProvider() };
        if (liveEndingProvider is not null) endingProviders.Add(liveEndingProvider);
        var careerEndingGeneration = new CareerEndingGenerationService(endingProviders, metrics);
        return new GameFixture(connection, database,
            new MockGameService(database, content, dialogueGeneration, careerEndingGeneration));
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        await connection.DisposeAsync();
    }
}

enum StubProviderBehavior { Success, TransientThenSuccess, PermanentFailure, NullCareerFeedback }

sealed class StubDialogueProvider(string providerId, StubProviderBehavior behavior) : IDialogueProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ProviderId { get; } = providerId;
    public int MaximumAttempts => 2;
    public StubProviderBehavior Behavior { get; } = behavior;
    public List<DialogueProviderRequest> Requests { get; } = [];

    public Task<string> GenerateAsync(DialogueProviderRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Behavior == StubProviderBehavior.TransientThenSuccess && request.Attempt == 1)
            throw new DialogueProviderException("temporary", true);
        if (Behavior == StubProviderBehavior.PermanentFailure)
            throw new DialogueProviderException("configuration", false);
        var careerFeedback = Behavior == StubProviderBehavior.NullCareerFeedback ? null : "대화 성향 근거를 남겼다.";
        var generated = new GeneratedDialogue("고정 계약을 지킨 Live 대사입니다.", "determined",
            "Live Provider 응답을 검증했다.", ["kind"], careerFeedback, "Live 응답 요약");
        return Task.FromResult(JsonSerializer.Serialize(generated, JsonOptions));
    }
}

enum CareerStubBehavior { Success, InvalidOccupation }

sealed class StubCareerEndingProvider(string providerId, CareerStubBehavior behavior) : ICareerEndingProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ProviderId { get; } = providerId;
    public int MaximumAttempts => 1;

    public Task<string> GenerateAsync(CareerEndingProviderRequest request, CancellationToken cancellationToken = default)
    {
        var occupation = behavior == CareerStubBehavior.InvalidOccupation
            ? "서버 판정을 무시한 떠돌이"
            : $"북부의 {request.Facts.TemplateOccupationName}";
        var generated = new GeneratedCareerEndingCandidates([
            new GeneratedCareerEnding(occupation,
                "영지는 위기를 넘겼다. 그는 자신의 선택을 직업으로 이어 갔다. 사람들은 그 기록을 오래 기억했다."),
            new GeneratedCareerEnding(occupation,
                $"{request.Facts.TerritoryOutcome}의 기록은 남았다. {request.Facts.CareerEvidence[0]} " +
                $"그 선택은 '{request.Facts.TemplateOccupationName}'라는 직업으로 이어졌다. 그의 용기는 오래 기억됐다."),
            new GeneratedCareerEnding(request.Facts.TemplateOccupationName,
                "북부는 다시 일어섰다. 그는 맡은 일을 계속했다. 후대는 그 이름을 기록했다.")
        ]);
        return Task.FromResult(JsonSerializer.Serialize(generated, JsonOptions));
    }
}

sealed class CapturingHttpHandler(Func<int, HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    public int CallCount { get; private set; }
    public string? RequestBody { get; private set; }
    public string? AuthorizationScheme { get; private set; }
    public Uri? RequestUri { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount += 1;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        AuthorizationScheme = request.Headers.Authorization?.Scheme;
        RequestUri = request.RequestUri;
        return responseFactory(CallCount, request);
    }
}

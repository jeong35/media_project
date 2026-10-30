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
        "community", "manual", "final_ward", false, "self_saved", "accomplished", "orthodox", "leadership", "북부 봉화술사"),
    new FateScenario("외부 지원", "prepare_evacuation", "seek_allies", "improve_farm_tools",
        "market", "tools", "final_external_aid", false, "external_aid", "accomplished", "unexpected", "diplomacy", "왕국 공인 농기구 외교관"),
    new FateScenario("전설 위상-봉인식", "prepare_evacuation", "research_ward", "test_magic_irrigation",
        "ward", "magic", "final_ward", false, "self_saved", "legendary", "prestige", "arcane", "세 봉화의 마탑주"),
    new FateScenario("B급 톤-마력 농기구", "prepare_evacuation", "research_ward", "improve_farm_tools",
        "ward", "tools", "final_ward", false, "self_saved", "accomplished", "b_grade", "arcane", "균열 봉합 농기구 명장"),
    new FateScenario("피해 생존", "prepare_evacuation", "research_ward", "farm_with_residents",
        "community", "manual", "final_defense", false, "damaged_survival", "survivor", "orthodox", "leadership", "피난민의 길잡이"),
    new FateScenario("시간 초과 대피", "repair_beacon", "research_ward", "test_magic_irrigation",
        "ward", "magic", "final_evacuate", true, "people_evacuated", "survivor", "orthodox", "arcane", "피난민의 길잡이"),
    new FateScenario("영지 멸망", "repair_beacon", "research_ward", "test_magic_irrigation",
        "ward", "magic", "final_defense", false, "destroyed", "fallen", "orthodox", "arcane", "유랑 봉화 연구자")
};

var content = MockContentLoader.Load();
var results = new List<TestResult>();
foreach (var scenario in scenarios)
    results.Add(await RunFateScenarioAsync(content, scenario));
results.Add(await RunTimeoutGuardAsync(content));
results.Add(await RunIdempotencyAsync(content));
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
results.Add(await RunOpenAiHttpContractAsync(content));
results.Add(await RunOpenAiQuotaFallbackAsync(content));
results.Add(await RunLiveCareerEndingScenarioAsync(content, CareerStubBehavior.Success,
    "openai", false, "북부의 구국의 기사단장"));
results.Add(await RunLiveCareerEndingScenarioAsync(content, CareerStubBehavior.InvalidOccupation,
    "fixed_fallback", true, "구국의 기사단장"));
results.Add(await RunMockCareerCandidateSelectionAsync());
results.Add(await RunOpenAiCareerEndingHttpContractAsync(content));
results.Add(await RunFarmingProgressRecoveryAsync(content));
results.Add(await RunFinalTimerRecoveryAsync(content));

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
        var choiceKey = NewKey();
        await fixture.Service.SubmitChoiceAsync(start.SessionId, new ChoiceRequest("rebuild_knights", choiceKey));
        await fixture.Service.SubmitChoiceAsync(start.SessionId, new ChoiceRequest("rebuild_knights", choiceKey));
        var snapshot = await fixture.Service.GetSessionAsync(start.SessionId);
        var actual = $"군사 {snapshot.State.Military}, 재정 {snapshot.State.Treasury}";
        var eventCount = await fixture.Database.GameEvents.CountAsync(gameEvent => gameEvent.SessionId == start.SessionId);
        var passed = actual == expected && eventCount == 3;
        return new TestResult("중복 요청 방지", expected, actual, $"이벤트 {eventCount}개", passed,
            passed ? null : "동일 idempotencyKey가 상태나 이벤트를 중복 적용했습니다.");
    }
    catch (Exception error)
    {
        return new TestResult("중복 요청 방지", expected, error.GetType().Name, "-", false, error.Message);
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
            && response.AttemptCount == 2 && response.ChoicesAvailable && snapshot.Phase == "AwaitingChoice";
        var expected = $"{expectedSource}/fallback={expectedFallback.ToString().ToLowerInvariant()}/선택 진행";
        var actual = $"{response.ResponseSource}/fallback={response.FallbackUsed.ToString().ToLowerInvariant()}/{snapshot.Phase}";
        return new TestResult($"대화 실패 복구-{scenario}", expected, actual, $"시도 {response.AttemptCount}회", passed,
            passed ? null : "재시도 또는 고정 대사 폴백 뒤 선택 단계로 진행하지 못했습니다.");
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
            && response.AttemptCount == expectedAttempts && snapshot.Phase == "AwaitingChoice";
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
        var provider = new OpenAiDialogueProvider(client, Options.Create(new OpenAiOptions
        {
            ApiKey = "test-placeholder", Model = "test-model", TimeoutSeconds = 2, MaxOutputTokens = 300
        }));
        var raw = await provider.GenerateAsync(new DialogueProviderRequest(content.GetAct(1), "주민을 먼저 지킨다.",
            "none", 1, ["kind", "pragmatic"]));
        var parsed = JsonSerializer.Deserialize<GeneratedDialogue>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var requestOk = handler.RequestBody?.Contains("\"json_schema\"") == true
            && handler.RequestBody.Contains("\"additionalProperties\":false")
            && !handler.RequestBody.Contains("maxLength")
            && handler.AuthorizationScheme == "Bearer" && handler.RequestUri?.AbsolutePath == "/v1/responses";
        var passed = handler.CallCount == 1 && requestOk && parsed?.Dialogue == generated.Dialogue;
        return new TestResult("OpenAI HTTP 계약", expected,
            passed ? expected : "request-or-response-mismatch", $"HTTP 호출 {handler.CallCount}회", passed,
            passed ? null : "Responses API 요청 스키마 또는 output_text 추출이 계약과 다릅니다.");
    }
    catch (Exception error)
    {
        return new TestResult("OpenAI HTTP 계약", expected, error.GetType().Name, "-", false, error.Message);
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
        var provider = new OpenAiDialogueProvider(client, Options.Create(new OpenAiOptions
        {
            ApiKey = "test-placeholder", Model = "test-model", TimeoutSeconds = 2
        }));
        await using var fixture = await GameFixture.CreateAsync(content, provider);
        var start = await fixture.Service.StartSessionAsync("live");
        var response = await fixture.Service.SubmitDialogueAsync(start.SessionId,
            new DialogueRequest(start.Scene.NpcId, "계획을 계속한다.", NewKey()));
        var actual = $"{response.ResponseSource}/시도 {response.AttemptCount}";
        var passed = actual == expected && response.FallbackUsed && handler.CallCount == 1;
        return new TestResult("OpenAI 영구 한도 오류", expected, actual, $"HTTP 호출 {handler.CallCount}회", passed,
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
        var service = new CareerEndingGenerationService([new MockCareerEndingProvider()]);
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
        var provider = new OpenAiCareerEndingProvider(client, Options.Create(new OpenAiOptions
        {
            ApiKey = "test-placeholder", Model = "test-model", TimeoutSeconds = 2, MaxOutputTokens = 300
        }));
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
        var passed = handler.CallCount == 1 && requestOk && parsed?.Candidates.Count == 3
            && parsed.Candidates[0].OccupationName == generated.Candidates[0].OccupationName;
        return new TestResult("OpenAI 직업 후일담 HTTP 계약", expected,
            passed ? expected : "request-or-response-mismatch", $"HTTP 호출 {handler.CallCount}회", passed,
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

static async Task<Guid> AdvanceToFarmingAsync(MockGameService game)
{
    var start = await game.StartSessionAsync("mock");
    await game.SubmitDialogueAsync(start.SessionId, new DialogueRequest(start.Scene.NpcId, "첫 결정을 내린다.", NewKey()));
    var act1 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest("prepare_evacuation", NewKey()));
    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act1.NextAct!.Scene.NpcId, "균열 대응책을 정한다.", NewKey()));
    var act2 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest("research_ward", NewKey()));
    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act2.NextAct!.Scene.NpcId, "주민의 밭을 돕는다.", NewKey()));
    var act3 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest("farm_with_residents", NewKey()));
    Assert(act3.NextActivity?.ActivityId == "farming", "농사 활동으로 전환되지 않았습니다.");
    return start.SessionId;
}

static async Task<FinalChoiceProgress> AdvanceToFinalChoiceAsync(MockGameService game, FateScenario scenario,
    string mode = "mock")
{
    var start = await game.StartSessionAsync(mode);
    await game.SubmitDialogueAsync(start.SessionId, new DialogueRequest(start.Scene.NpcId, "첫 결정을 내린다.", NewKey()));
    var act1 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest(scenario.Act1Choice, NewKey()));
    Assert(act1.NextAct?.Act == 2, "Act 2로 전환되지 않았습니다.");

    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act1.NextAct!.Scene.NpcId, "균열 대응책을 정한다.", NewKey()));
    var act2 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest(scenario.Act2Choice, NewKey()));
    Assert(act2.NextAct?.Act == 3, "Act 3으로 전환되지 않았습니다.");

    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(act2.NextAct!.Scene.NpcId, "주민의 밭을 돕는다.", NewKey()));
    var act3 = await game.SubmitChoiceAsync(start.SessionId, new ChoiceRequest(scenario.Act3Choice, NewKey()));
    Assert(act3.NextActivity?.ActivityId == "farming", "농사 활동으로 전환되지 않았습니다.");

    var farming = await game.SubmitFarmingAsync(start.SessionId,
        new FarmingResultRequest(3, 3, scenario.WaterTarget, scenario.Method, NewKey()));
    Assert(farming.Score == 90, "농사 점수가 90점이 아닙니다.");
    Assert(farming.NextAct?.Act == 4, "Act 4로 전환되지 않았습니다.");

    await game.SubmitDialogueAsync(start.SessionId,
        new DialogueRequest(farming.NextAct!.Scene.NpcId, "최종 명령을 내린다.", NewKey()));
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
        ICareerEndingProvider? liveEndingProvider = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GameDbContext>().UseSqlite(connection).Options;
        var database = new GameDbContext(options);
        await database.Database.EnsureCreatedAsync();
        var providers = new List<IDialogueProvider> { new MockDialogueProvider() };
        if (liveProvider is not null) providers.Add(liveProvider);
        var dialogueGeneration = new DialogueGenerationService(providers);
        var endingProviders = new List<ICareerEndingProvider> { new MockCareerEndingProvider() };
        if (liveEndingProvider is not null) endingProviders.Add(liveEndingProvider);
        var careerEndingGeneration = new CareerEndingGenerationService(endingProviders);
        return new GameFixture(connection, database,
            new MockGameService(database, content, dialogueGeneration, careerEndingGeneration));
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        await connection.DisposeAsync();
    }
}

enum StubProviderBehavior { Success, TransientThenSuccess, PermanentFailure }

sealed class StubDialogueProvider(string providerId, StubProviderBehavior behavior) : IDialogueProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ProviderId { get; } = providerId;
    public int MaximumAttempts => 2;
    public StubProviderBehavior Behavior { get; } = behavior;

    public Task<string> GenerateAsync(DialogueProviderRequest request, CancellationToken cancellationToken = default)
    {
        if (Behavior == StubProviderBehavior.TransientThenSuccess && request.Attempt == 1)
            throw new DialogueProviderException("temporary", true);
        if (Behavior == StubProviderBehavior.PermanentFailure)
            throw new DialogueProviderException("configuration", false);
        var generated = new GeneratedDialogue("고정 계약을 지킨 Live 대사입니다.", "determined",
            "Live Provider 응답을 검증했다.", ["kind"], "대화 성향 근거를 남겼다.", "Live 응답 요약");
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

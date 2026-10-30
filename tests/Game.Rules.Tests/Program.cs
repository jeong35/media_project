using Game.Api.Contracts;
using Game.Api.Data;
using Game.Api.Errors;
using Game.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var scenarios = new[]
{
    new FateScenario("자력 생존-기사단", "rebuild_knights", "train_defenders", "farm_with_residents",
        "community", "manual", "final_defense", false, "self_saved", "accomplished", "martial", "균열 수비대장"),
    new FateScenario("자력 생존-봉인식", "prepare_evacuation", "research_ward", "farm_with_residents",
        "community", "manual", "final_ward", false, "self_saved", "accomplished", "leadership", "북부 봉화술사"),
    new FateScenario("외부 지원", "prepare_evacuation", "seek_allies", "improve_farm_tools",
        "market", "tools", "final_external_aid", false, "external_aid", "accomplished", "diplomacy", "북부 연합 조정관"),
    new FateScenario("피해 생존", "prepare_evacuation", "research_ward", "farm_with_residents",
        "community", "manual", "final_defense", false, "damaged_survival", "survivor", "leadership", "피난민의 길잡이"),
    new FateScenario("시간 초과 대피", "repair_beacon", "research_ward", "test_magic_irrigation",
        "ward", "magic", "final_evacuate", true, "people_evacuated", "survivor", "arcane", "피난민의 길잡이"),
    new FateScenario("영지 멸망", "repair_beacon", "research_ward", "test_magic_irrigation",
        "ward", "magic", "final_defense", false, "destroyed", "fallen", "arcane", "유랑 봉화 연구자")
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
            && ending.EndingTone == "orthodox" && ending.CareerEvidence?.Count >= 2;
        var expected = $"{scenario.ExpectedFate}/{scenario.ExpectedRank}/{scenario.ExpectedOccupation}";
        var actualSummary = $"{actual}/{ending.CareerRank}/{ending.OccupationName}";
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
            "ward", "magic", "final_defense", true, expected, "", "", "");
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
            && ending.TopCareerAffinities is null && ending.CareerEvidence is null;
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

static async Task<FinalChoiceProgress> AdvanceToFinalChoiceAsync(MockGameService game, FateScenario scenario)
{
    var start = await game.StartSessionAsync("mock");
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
    string ExpectedRank, string ExpectedDominantAffinity, string ExpectedOccupation);
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

    public static async Task<GameFixture> CreateAsync(MockGameContent content)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GameDbContext>().UseSqlite(connection).Options;
        var database = new GameDbContext(options);
        await database.Database.EnsureCreatedAsync();
        var dialogueGeneration = new DialogueGenerationService(new MockDialogueProvider());
        return new GameFixture(connection, database, new MockGameService(database, content, dialogueGeneration));
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        await connection.DisposeAsync();
    }
}

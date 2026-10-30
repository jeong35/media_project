using System.Text.Json;
using Game.Api.Contracts;

namespace Game.Api.Services;

public sealed record CareerEndingFacts(string TemplateOccupationName, string Title, string TerritoryOutcome,
    string TerritoryFateId, string CareerRank, string EndingTone,
    IReadOnlyList<CareerAffinityResponse> TopCareerAffinities, IReadOnlyList<string> CareerEvidence,
    IReadOnlyList<string> Traits);
public sealed record GeneratedCareerEnding(string OccupationName, string Epilogue);
public sealed record GeneratedCareerEndingCandidates(IReadOnlyList<GeneratedCareerEnding> Candidates);
public sealed record CareerEndingGenerationResult(GeneratedCareerEnding Content, string ResponseSource,
    bool FallbackUsed, int AttemptCount, int CandidateCount, int SelectedCandidateIndex, int SelectionScore);
public sealed record CareerEndingProviderRequest(CareerEndingFacts Facts, int Attempt);

public interface ICareerEndingProvider
{
    string ProviderId { get; }
    int MaximumAttempts { get; }
    Task<string> GenerateAsync(CareerEndingProviderRequest request, CancellationToken cancellationToken = default);
}

public sealed class CareerEndingProviderException(string message, bool retryable, TimeSpan? retryAfter = null,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool Retryable { get; } = retryable;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class MockCareerEndingProvider : ICareerEndingProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ProviderId => "mock";
    public int MaximumAttempts => 1;

    public Task<string> GenerateAsync(CareerEndingProviderRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(JsonSerializer.Serialize(CareerEndingGenerationService.BuildMockCandidates(request.Facts), JsonOptions));
}

public sealed class CareerEndingGenerationService(IEnumerable<ICareerEndingProvider> providers)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, ICareerEndingProvider> providersById = providers
        .ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);

    public async Task<CareerEndingGenerationResult> GenerateAsync(string providerId, CareerEndingFacts facts,
        CancellationToken cancellationToken = default)
    {
        if (!providersById.TryGetValue(providerId, out var provider)) return FixedFallback(facts, 0);

        var maximumAttempts = Math.Clamp(provider.MaximumAttempts, 1, 3);
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                var raw = await provider.GenerateAsync(new CareerEndingProviderRequest(facts, attempt), cancellationToken);
                var generated = JsonSerializer.Deserialize<GeneratedCareerEndingCandidates>(raw, JsonOptions)
                    ?? throw new JsonException("Career ending payload was empty.");
                Validate(generated, facts);
                var selection = generated.Candidates
                    .Select((candidate, index) => new CandidateSelection(candidate, index, Score(candidate, facts)))
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Index)
                    .First();
                return new CareerEndingGenerationResult(selection.Content, provider.ProviderId, false, attempt,
                    generated.Candidates.Count, selection.Index, selection.Score);
            }
            catch (CareerEndingProviderException error) when (!error.Retryable)
            {
                return FixedFallback(facts, attempt);
            }
            catch (Exception error) when (error is HttpRequestException or TimeoutException or JsonException or InvalidDataException)
            {
                // Retry a transient failure or an invalid payload before selecting fixed content.
            }
            catch (CareerEndingProviderException error)
            {
                if (attempt >= maximumAttempts) continue;
                var delay = error.RetryAfter ?? TimeSpan.FromMilliseconds(100 * attempt);
                if (delay > TimeSpan.FromSeconds(5)) return FixedFallback(facts, attempt);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            }
        }

        return FixedFallback(facts, maximumAttempts);
    }

    public static GeneratedCareerEnding BuildFixedContent(CareerEndingFacts facts)
    {
        var evidence = facts.CareerEvidence.FirstOrDefault() ?? "위기 속에서 자신의 역할을 끝까지 다했다.";
        var traits = string.Join("과 ", facts.Traits.Take(2));
        var epilogue = $"{facts.TerritoryOutcome}의 기록은 오래 남았다. {evidence} " +
            $"그 결과 그는 '{facts.TemplateOccupationName}'라는 이름으로 불렸다. " +
            $"그의 {traits}은 다음 세대가 북부를 다시 세우는 기준이 되었다.";
        return new GeneratedCareerEnding(facts.TemplateOccupationName, epilogue);
    }

    public static GeneratedCareerEndingCandidates BuildMockCandidates(CareerEndingFacts facts)
    {
        var firstEvidence = facts.CareerEvidence.ElementAtOrDefault(0) ?? "위기 속에서 자신의 역할을 끝까지 다했다.";
        var secondEvidence = facts.CareerEvidence.ElementAtOrDefault(1) ?? firstEvidence;
        var traits = facts.Traits.Take(2).ToArray();
        var firstTrait = traits.ElementAtOrDefault(0) ?? "용기";
        var secondTrait = traits.ElementAtOrDefault(1) ?? firstTrait;
        return new GeneratedCareerEndingCandidates([
            BuildFixedContent(facts),
            new GeneratedCareerEnding(facts.TemplateOccupationName,
                $"{facts.TerritoryOutcome}의 기록은 오래 남았다. {firstEvidence} {secondEvidence} " +
                $"이 두 선택은 그가 '{facts.TemplateOccupationName}'로 불리게 된 직접적인 근거가 되었다. " +
                $"그의 {firstTrait}과 {secondTrait}은 다음 세대의 기준이 되었다."),
            new GeneratedCareerEnding(facts.TemplateOccupationName,
                $"북부 사람들은 위기가 지나간 뒤에도 그를 기억했다. 그는 '{facts.TemplateOccupationName}'라는 이름을 받아들였다. " +
                $"그의 {firstTrait}은 오래도록 사람들에게 전해졌다. 마침내 새로운 시대가 열렸다.")
        ]);
    }

    private static CareerEndingGenerationResult FixedFallback(CareerEndingFacts facts, int attemptCount) =>
        new(BuildFixedContent(facts), "fixed_fallback", true, attemptCount, 0, -1, 0);

    private static void Validate(GeneratedCareerEndingCandidates generated, CareerEndingFacts facts)
    {
        if (generated.Candidates is null || generated.Candidates.Count != 3)
            throw new InvalidDataException("Career ending provider must return exactly three candidates.");
        foreach (var candidate in generated.Candidates) ValidateCandidate(candidate, facts);
        if (generated.Candidates.Select(candidate => $"{candidate.OccupationName}\n{candidate.Epilogue}")
            .Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidDataException("Career ending candidates must be distinct.");
    }

    private static void ValidateCandidate(GeneratedCareerEnding generated, CareerEndingFacts facts)
    {
        if (string.IsNullOrWhiteSpace(generated.OccupationName) || string.IsNullOrWhiteSpace(generated.Epilogue))
            throw new InvalidDataException("Career ending payload did not satisfy the required contract.");
        if (generated.OccupationName.Length > 30
            || !generated.OccupationName.Contains(facts.TemplateOccupationName, StringComparison.Ordinal)
            || generated.Epilogue.Length > 800)
            throw new InvalidDataException("Career ending changed the fixed occupation or exceeded length limits.");

        var sentenceCount = generated.Epilogue.Count(character => character is '.' or '!' or '?');
        if (sentenceCount is < 3 or > 5)
            throw new InvalidDataException("Career ending epilogue must contain three to five sentences.");
    }

    private static int Score(GeneratedCareerEnding candidate, CareerEndingFacts facts)
    {
        var score = candidate.OccupationName.Equals(facts.TemplateOccupationName, StringComparison.Ordinal) ? 2 : 1;
        if (candidate.Epilogue.Contains(facts.TerritoryOutcome, StringComparison.Ordinal)) score += 4;
        score += facts.CareerEvidence.Count(evidence => ContainsFact(candidate.Epilogue, evidence)) * 3;
        score += facts.Traits.Count(trait => ContainsFact(candidate.Epilogue, trait)) * 2;
        return score;
    }

    private static bool ContainsFact(string epilogue, string fact)
    {
        var normalized = fact.Trim().TrimEnd('.', '!', '?');
        return normalized.Length >= 2 && epilogue.Contains(normalized, StringComparison.Ordinal);
    }

    private sealed record CandidateSelection(GeneratedCareerEnding Content, int Index, int Score);
}

using System.Text.Json;
using Game.Api.Contracts;

namespace Game.Api.Services;

public sealed record CareerEndingFacts(string TemplateOccupationName, string Title, string TerritoryOutcome,
    string TerritoryFateId, string CareerRank, string EndingTone,
    IReadOnlyList<CareerAffinityResponse> TopCareerAffinities, IReadOnlyList<string> CareerEvidence,
    IReadOnlyList<string> Traits);
public sealed record GeneratedCareerEnding(string OccupationName, string Epilogue);
public sealed record CareerEndingGenerationResult(GeneratedCareerEnding Content, string ResponseSource,
    bool FallbackUsed, int AttemptCount);
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
        Task.FromResult(JsonSerializer.Serialize(CareerEndingGenerationService.BuildFixedContent(request.Facts), JsonOptions));
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
                var generated = JsonSerializer.Deserialize<GeneratedCareerEnding>(raw, JsonOptions)
                    ?? throw new JsonException("Career ending payload was empty.");
                Validate(generated, facts);
                return new CareerEndingGenerationResult(generated, provider.ProviderId, false, attempt);
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

    private static CareerEndingGenerationResult FixedFallback(CareerEndingFacts facts, int attemptCount) =>
        new(BuildFixedContent(facts), "fixed_fallback", true, attemptCount);

    private static void Validate(GeneratedCareerEnding generated, CareerEndingFacts facts)
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
}

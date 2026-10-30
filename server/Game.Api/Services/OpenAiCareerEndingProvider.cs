using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Game.Api.Services;

public sealed class OpenAiCareerEndingProvider(HttpClient httpClient, IOptions<OpenAiOptions> options) : ICareerEndingProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiOptions settings = options.Value;
    public string ProviderId => "openai";
    public int MaximumAttempts => Math.Clamp(settings.MaxRetries, 0, 2) + 1;

    public async Task<string> GenerateAsync(CareerEndingProviderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Model))
            throw new CareerEndingProviderException("OpenAI provider is not configured.", false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 60)));
        using var message = new HttpRequestMessage(HttpMethod.Post, "responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        message.Content = JsonContent.Create(BuildRequest(request.Facts), options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CareerEndingProviderException("OpenAI request timed out.", true, innerException: error);
        }
        catch (HttpRequestException error)
        {
            throw new CareerEndingProviderException("OpenAI request could not be completed.", true, innerException: error);
        }

        using (response)
        {
            var responseJson = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode) throw CreateHttpError(response, responseJson);
            return ExtractOutputText(responseJson);
        }
    }

    private object BuildRequest(CareerEndingFacts facts) => new
    {
        model = settings.Model,
        input = new object[]
        {
            new
            {
                role = "developer",
                content = "당신은 한국어 판타지 게임의 엔딩 문구 편집자다. 서버가 확정한 영지 운명, 직업 위상, 엔딩 톤과 업적을 바꾸지 않는다. " +
                    "서로 다른 후보를 정확히 3개 만든다. 각 occupationName에는 기본 직업명을 정확히 포함하고 30자 이내로 표현한다. " +
                    "각 epilogue는 제공된 사실만 사용해 3~5문장으로 작성하고, 플레이 근거와 직업 사이의 인과관계를 설명한다."
            },
            new
            {
                role = "user",
                content = JsonSerializer.Serialize(facts, JsonOptions)
            }
        },
        max_output_tokens = Math.Clamp(settings.CareerEndingMaxOutputTokens, 300, 4_000),
        text = new
        {
            format = new
            {
                type = "json_schema",
                name = "career_ending_expression",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["candidates"] = new
                        {
                            type = "array",
                            minItems = 3,
                            maxItems = 3,
                            items = new
                            {
                                type = "object",
                                properties = new Dictionary<string, object>
                                {
                                    ["occupationName"] = new { type = "string" },
                                    ["epilogue"] = new { type = "string" }
                                },
                                required = new[] { "occupationName", "epilogue" },
                                additionalProperties = false
                            }
                        }
                    },
                    required = new[] { "candidates" },
                    additionalProperties = false
                }
            }
        }
    };

    private static CareerEndingProviderException CreateHttpError(HttpResponseMessage response, string responseJson)
    {
        var errorCode = TryReadErrorCode(responseJson);
        var permanentRateLimit = errorCode is "credit_balance_exhausted" or "organization_spend_limit_exceeded"
            or "project_spend_limit_exceeded" or "organization_usage_limit_exceeded";
        var retryable = !permanentRateLimit && response.StatusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
            retryAfter = retryDate - DateTimeOffset.UtcNow;
        return new CareerEndingProviderException($"OpenAI request failed with HTTP {(int)response.StatusCode}.",
            retryable, retryAfter > TimeSpan.Zero ? retryAfter : null);
    }

    private static string? TryReadErrorCode(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractOutputText(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("status", out var status) || status.GetString() != "completed")
            throw new InvalidDataException("OpenAI response did not complete.");
        if (!document.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("OpenAI response did not contain output items.");

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemType) || itemType.GetString() != "message"
                || !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var part in content.EnumerateArray())
                if (part.TryGetProperty("type", out var partType) && partType.GetString() == "output_text"
                    && part.TryGetProperty("text", out var text) && !string.IsNullOrWhiteSpace(text.GetString()))
                    return text.GetString()!;
        }

        throw new InvalidDataException("OpenAI response did not contain structured output text.");
    }
}

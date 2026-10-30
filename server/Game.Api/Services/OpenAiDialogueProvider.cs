using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Game.Api.Services;

public sealed class OpenAiOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 12;
    public int MaxOutputTokens { get; init; } = 500;
    public int CareerEndingMaxOutputTokens { get; init; } = 1_200;
    public int MaxRetries { get; init; } = 1;
}

public sealed class OpenAiDialogueProvider(HttpClient httpClient, IOptions<OpenAiOptions> options) : IDialogueProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiOptions settings = options.Value;
    public string ProviderId => "openai";
    public int MaximumAttempts => Math.Clamp(settings.MaxRetries, 0, 2) + 1;

    public async Task<string> GenerateAsync(DialogueProviderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.Model))
            throw new DialogueProviderException("OpenAI provider is not configured.", false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 60)));
        using var message = new HttpRequestMessage(HttpMethod.Post, "responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        message.Content = JsonContent.Create(BuildRequest(request), options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DialogueProviderException("OpenAI request timed out.", true, innerException: error);
        }
        catch (HttpRequestException error)
        {
            throw new DialogueProviderException("OpenAI request could not be completed.", true, innerException: error);
        }

        using (response)
        {
            var responseJson = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw CreateHttpError(response, responseJson);

            return ExtractOutputText(responseJson);
        }
    }

    private object BuildRequest(DialogueProviderRequest request) => new
    {
        model = settings.Model,
        input = new object[]
        {
            new
            {
                role = "developer",
                content = "당신은 한국어 판타지 게임의 NPC 대사 생성기다. 주어진 장면과 플레이어 발화에만 반응한다. " +
                    "능력치 변화, 성공 여부, 다음 Act, 영지 운명과 직업을 결정하지 않는다. 허용된 성향 태그만 반환한다."
            },
            new
            {
                role = "user",
                content = $"NPC: {request.Act.Scene.NpcName}\n장면: {request.Act.Scene.Narration}\n" +
                    $"NPC의 첫 대사: {request.Act.Scene.OpeningDialogue}\n플레이어 발화: {request.PlayerText}"
            }
        },
        max_output_tokens = Math.Clamp(settings.MaxOutputTokens, 100, 2_000),
        text = new
        {
            format = new
            {
                type = "json_schema",
                name = "npc_dialogue",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["dialogue"] = new { type = "string" },
                        ["emotion"] = new { type = "string" },
                        ["feedback"] = new { type = "string" },
                        ["acceptedTags"] = new
                        {
                            type = "array",
                            items = new { type = "string", @enum = request.AllowedTags },
                            maxItems = 3
                        },
                        ["careerFeedback"] = new { type = "string" },
                        ["summary"] = new { type = "string" }
                    },
                    required = new[] { "dialogue", "emotion", "feedback", "acceptedTags", "careerFeedback", "summary" },
                    additionalProperties = false
                }
            }
        }
    };

    private static DialogueProviderException CreateHttpError(HttpResponseMessage response, string responseJson)
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
        return new DialogueProviderException($"OpenAI request failed with HTTP {(int)response.StatusCode}.",
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

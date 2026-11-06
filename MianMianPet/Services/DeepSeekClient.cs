using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MianMianPet.Models;

namespace MianMianPet.Services;

public sealed class DeepSeekClient
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly PetPersona _persona;

    public DeepSeekClient(
        HttpClient httpClient,
        SettingsService settingsService,
        PetPersona persona)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _persona = persona;
    }

    public async IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!InternalAiConfig.IsConfigured)
        {
            throw new InvalidOperationException("内置 AI 服务尚未完成配置。 ");
        }

        var settings = _settingsService.Current;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildEndpoint(InternalAiConfig.BaseUrl, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InternalAiConfig.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var apiMessages = new List<object>
        {
            new { role = "system", content = _persona.Render(settings) }
        };
        apiMessages.AddRange(messages.Select(message => (object)new
        {
            role = message.Role,
            content = message.Content
        }));

        var body = new
        {
            model = InternalAiConfig.Model,
            messages = apiMessages,
            stream = true,
            max_tokens = InternalAiConfig.MaxTokens,
            temperature = InternalAiConfig.Temperature,
            thinking = new { type = "disabled" }
        };
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data == "[DONE]")
            {
                yield break;
            }

            if (TryReadContent(data, out var content) && !string.IsNullOrEmpty(content))
            {
                yield return content;
            }
        }
    }

    private static Uri BuildEndpoint(string baseUrl, string relativePath) =>
        new($"{baseUrl.Trim().TrimEnd('/')}/{relativePath}", UriKind.Absolute);

    private static bool TryReadContent(string json, out string? content)
    {
        content = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return false;
            }

            if (!choices[0].TryGetProperty("delta", out var delta) ||
                delta.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (delta.TryGetProperty("content", out var value) && value.ValueKind == JsonValueKind.String)
            {
                content = value.GetString();
                return true;
            }
        }
        catch (JsonException)
        {
            // Ignore malformed or provider-specific SSE housekeeping lines.
        }

        return false;
    }

    private static async Task<DeepSeekApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "内置 AI 服务验证失败，请联系构建者检查配置。",
            HttpStatusCode.PaymentRequired => "API 余额不足，请充值后再试。",
            HttpStatusCode.TooManyRequests => "今天有点挤，稍后再来找我吧。",
            HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable =>
                "服务刚刚打了个喷嚏，稍后再试一次吧。",
            _ => $"DeepSeek 请求失败（HTTP {(int)response.StatusCode}）。"
        };

        _ = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new DeepSeekApiException(response.StatusCode, message);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MianMianPet.Configuration;
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
        if (!LocalConfig.Current.Ai.IsConfigured)
        {
            throw new InvalidOperationException("请在本机 config.json 中填写 ai.apiKey 后重启。");
        }

        var settings = _settingsService.Current;

        var apiMessages = new List<object>
        {
            new { role = "system", content = _persona.Render(settings) }
        };
        apiMessages.AddRange(messages.Select(message => (object)new
        {
            role = message.Role,
            content = message.Content
        }));

        var reply = new StringBuilder();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildEndpoint(LocalConfig.Current.Ai.BaseUrl, "chat/completions"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", LocalConfig.Current.Ai.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            var body = new
            {
                model = LocalConfig.Current.Ai.Model,
                messages = apiMessages,
                stream = true,
                max_tokens = LocalConfig.Current.Ai.MaxTokens,
                temperature = LocalConfig.Current.Ai.Temperature,
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
            string? finishReason = null;
            var receivedDone = false;
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = line[5..].Trim();
                if (data == "[DONE]")
                {
                    receivedDone = true;
                    break;
                }

                if (TryReadContent(data, out var content, out var reason))
                {
                    finishReason = reason ?? finishReason;
                    if (!string.IsNullOrEmpty(content))
                    {
                        reply.Append(content);
                        yield return content;
                    }
                }
            }

            if (finishReason == "stop" || (receivedDone && finishReason is null)) yield break;
            if (finishReason != "length" || attempt == 1)
            {
                throw new InvalidDataException("AI 回复尚未完整结束。");
            }

            // Continue a capped answer once, within the same request timeout and conversation exchange.
            apiMessages.Add(new { role = "assistant", content = reply.ToString() });
            apiMessages.Add(new { role = "user", content = "请紧接着刚才未说完的地方，用一两句把这次回答收好，不要重述已有内容。" });
        }
    }

    private static Uri BuildEndpoint(string baseUrl, string relativePath) =>
        new($"{baseUrl.Trim().TrimEnd('/')}/{relativePath}", UriKind.Absolute);

    private static bool TryReadContent(string json, out string? content, out string? finishReason)
    {
        content = null;
        finishReason = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return false;
            }

            if (choices[0].TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            {
                finishReason = reason.GetString();
            }

            if (!choices[0].TryGetProperty("delta", out var delta) ||
                delta.ValueKind != JsonValueKind.Object)
            {
                return finishReason is not null;
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

        return finishReason is not null;
    }

    private static async Task<DeepSeekApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "AI 服务验证失败，请检查本机 config.json 的 ai 配置。",
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

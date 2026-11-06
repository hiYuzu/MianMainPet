using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using MianMianPet.Configuration;
using MianMianPet.Infrastructure;
using MianMianPet.Models;
using MianMianPet.Services;

namespace MianMianPet.Tests;

internal static class ConfigTests
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        var paths = new AppPaths(Path.GetFullPath(Path.Combine(output, Guid.NewGuid().ToString("N"))));
        var log = new LogService(paths);
        await LocalConfig.LoadAsync(paths, log);
        var template = await File.ReadAllTextAsync(paths.ConfigFile);
        using (var document = JsonDocument.Parse(template))
        {
            check(document.RootElement.GetProperty("ai").GetProperty("apiKey").GetString() == "", "new configuration has no key");
            check(document.RootElement.GetProperty("experience").GetProperty("ambientIdleDelay").GetString() == "00:00:10", "template contains editable experience defaults");
            check(document.RootElement.GetProperty("personaPrompt").GetString()!.Contains("{PetName}"), "template includes the full default persona");
        }
        await LocalConfig.LoadAsync(paths, log);
        check(!LocalConfig.Current.Ai.IsConfigured && await File.ReadAllTextAsync(paths.ConfigFile) == template, "default template loads without being rewritten");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "config.example.json"), template);

        // Existing settings and celebrations must remain independent of technical configuration.
        const string oldSettings = "{\"PetName\":\"棉花\",\"UserTitle\":\"朋友\",\"AlwaysOnTop\":false}";
        const string oldCelebrations = "[]";
        await File.WriteAllTextAsync(paths.SettingsFile, oldSettings);
        await File.WriteAllTextAsync(paths.CelebrationsFile, oldCelebrations);
        const string testKey = "test-only-local-credential";
        await File.WriteAllTextAsync(paths.ConfigFile, "{\"ai\":{\"apiKey\":\"  " + testKey + "  \"}}");
        await LocalConfig.LoadAsync(paths, log);
        check(LocalConfig.Current.Ai.IsConfigured && LocalConfig.Current.Ai.ApiKey == testKey &&
            LocalConfig.Current.Ai.Model == "deepseek-flash" && LocalConfig.Current.Experience.PlayMinimumIdle.TotalSeconds == 45,
            "key-only JSON uses defaults for omitted fields and trims the key");
        var settings = new SettingsService(paths, log);
        await settings.LoadAsync();
        check(settings.Current.PetName == "棉花" && !settings.Current.AlwaysOnTop &&
            await File.ReadAllTextAsync(paths.SettingsFile) == oldSettings &&
            await File.ReadAllTextAsync(paths.CelebrationsFile) == oldCelebrations, "existing settings and celebrations are preserved");

        const string custom = """
            {
              "ai": {
                "apiKey": "test-only-local-credential", "baseUrl": "https://example.invalid/v1/",
                "model": "configured-model", "maxTokens": 123, "temperature": 0.3,
                "contextMessageCount": 2, "maxContextCharacters": 20, "bubbleAutoHideSeconds": 9
              },
              "experience": {
                "ambientIdleDelay": "00:00:05", "sleepIdleDelay": "00:00:20",
                "aiRequestTimeout": "00:00:30", "dayCompanionReplies": ["你好，{UserTitle}", "你好，{UserTitle}"]
              },
              "personaPrompt": "你是{PetName}，正在陪{UserTitle}。",
              "futureField": "preserved"
            }
            """;
        await File.WriteAllTextAsync(paths.ConfigFile, custom);
        await LocalConfig.LoadAsync(paths, log);
        check(LocalConfig.Current.Experience.AmbientIdleDelay.TotalSeconds == 5 &&
            LocalConfig.Current.Experience.AiRequestTimeout.TotalSeconds == 30 && LocalConfig.Current.Ai.BubbleAutoHideSeconds == 9,
            "custom timing and AI parameters take effect");
        check(LocalConfig.Current.Experience.GetCompanionReply(new Random(1), new DateTime(2026, 11, 6, 12, 0, 0),
            "朋友", "棉花", "你好，朋友") == "你好，朋友", "duplicate custom replies do not break companion selection");
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var client = new DeepSeekClient(http, settings, new PetPersona(LocalConfig.Current.PersonaPrompt));
        var reply = "";
        await foreach (var piece in client.StreamReplyAsync([ChatMessage.User("hello")], CancellationToken.None)) reply += piece;
        check(reply == "configured reply" && handler.RequestUri == "https://example.invalid/v1/chat/completions" &&
            handler.Authorization == "Bearer " + testKey, "HTTP request uses local endpoint and credential without external network access");
        check(handler.Body.GetProperty("model").GetString() == "configured-model" &&
            handler.Body.GetProperty("max_tokens").GetInt32() == 123 && handler.Body.GetProperty("temperature").GetDouble() == 0.3 &&
            handler.Body.GetProperty("messages")[0].GetProperty("content").GetString() == "你是棉花，正在陪朋友。",
            "HTTP body uses configured model, generation parameters and persona");
        var database = new DatabaseService(paths, log);
        await database.InitializeAsync();
        var conversation = new ConversationService(database);
        await conversation.SaveExchangeAsync(ChatMessage.User("old"), ChatMessage.Assistant("old reply"));
        await conversation.SaveExchangeAsync(ChatMessage.User("new"), ChatMessage.Assistant("new reply"));
        var context = await conversation.BuildContextAsync(ChatMessage.User("now"));
        check(context.Count == 3 && context[0].Content == "new", "configured context message limit reaches conversation service");
        await File.WriteAllTextAsync(paths.ConfigFile, custom.Replace("\"maxContextCharacters\": 20", "\"maxContextCharacters\": 10"));
        await LocalConfig.LoadAsync(paths, log);
        context = await conversation.BuildContextAsync(ChatMessage.User("now"));
        check(context.Count == 1 && context[0].Content == "now", "configured character budget limits history without losing current input");
        await File.WriteAllTextAsync(paths.ConfigFile, custom);
        await LocalConfig.LoadAsync(paths, log);
        await log.ErrorAsync("test_mask", new Exception("credential=" + testKey));
        await settings.SaveAsync(settings.Current);
        check(await File.ReadAllTextAsync(paths.ConfigFile) == custom, "saving UI settings does not rewrite config or drop unknown fields");

        var invalidCases = new[]
        {
            "{\"" + testKey + "\": [}", "null", "[]", "{\"ai\":null}", "{\"experience\":null}",
            "{\"ai\":{\"baseUrl\":\"file:///tmp\"}}", "{\"ai\":{\"apiKey\":\"bad key\"}}",
            "{\"ai\":{\"maxTokens\":0}}", "{\"ai\":{\"temperature\":3}}", "{\"personaPrompt\":null}",
            "{\"ai\":{\"contextMessageCount\":0}}", "{\"ai\":{\"contextMessageCount\":51}}",
            "{\"experience\":{\"aiRequestTimeout\":\"00:00:00\"}}",
            "{\"experience\":{\"sleepIdleDelay\":\"00:00:01\"}}",
            "{\"experience\":{\"playMinimumDelay\":\"00:20:00\"}}",
            "{\"experience\":{\"nightStartHour\":25}}",
            "{\"experience\":{\"dayCompanionReplies\":[]}}",
            "{\"experience\":{\"dayCompanionReplies\":[null]}}"
        };
        for (var i = 0; i < invalidCases.Length; i++)
        {
            await File.WriteAllTextAsync(paths.ConfigFile, custom);
            await LocalConfig.LoadAsync(paths, log);
            await File.WriteAllTextAsync(paths.ConfigFile, invalidCases[i]);
            await LocalConfig.LoadAsync(paths, log);
            check(!LocalConfig.Current.Ai.IsConfigured && LocalConfig.Current.Experience.AmbientIdleDelay.TotalSeconds == 10 &&
                await File.ReadAllTextAsync(paths.ConfigFile) == invalidCases[i], $"invalid config case {i + 1} disables AI and preserves original file");
        }
        await File.WriteAllTextAsync(paths.ConfigFile, custom);
        using (var locked = new FileStream(paths.ConfigFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await LocalConfig.LoadAsync(paths, log);
            check(!LocalConfig.Current.Ai.IsConfigured, "locked configuration does not prevent offline startup");
        }
        await LocalConfig.LoadAsync(paths, log);
        check(LocalConfig.Current.Ai.IsConfigured, "corrected configuration recovers on next startup load");
        var logs = string.Join("\n", await Task.WhenAll(Directory.GetFiles(paths.Logs).Select(file => File.ReadAllTextAsync(file))));
        check(!logs.Contains(testKey) && logs.Contains("[REDACTED]") && logs.Contains("config_load_failed"), "logs contain diagnostics without credentials or raw malformed JSON");

        await File.WriteAllTextAsync(paths.ConfigFile, "{}");
        await LocalConfig.LoadAsync(paths, log);
        var requestsBefore = handler.Requests;
        var missingKeyRejected = false;
        try
        {
            await foreach (var _ in client.StreamReplyAsync([ChatMessage.User("hello")], CancellationToken.None)) { }
        }
        catch (InvalidOperationException) { missingKeyRejected = true; }
        check(missingKeyRejected && handler.Requests == requestsBefore, "missing key sends no HTTP request");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        internal string? RequestUri { get; private set; }
        internal string? Authorization { get; private set; }
        internal JsonElement Body { get; private set; }
        internal int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            RequestUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Body = document.RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"configured reply\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n")
            };
        }
    }
}

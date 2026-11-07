using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Data.Sqlite;
using MianMianPet.Configuration;
using MianMianPet.Infrastructure;
using MianMianPet.Models;
using MianMianPet.Services;

namespace MianMianPet.Tests;

internal static class ServiceRegressionTests
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        var paths = new AppPaths(Path.GetFullPath(Path.Combine(output, Guid.NewGuid().ToString("N"))));
        var log = new LogService(paths);
        await TestCorruptionRecoveryAsync(paths, log, check);
        await TestLegacyMigrationAsync(output, check);
        await TestCelebrationsAsync(output, check);
        await TestStreamingAsync(output, check);
    }

    private static async Task TestCorruptionRecoveryAsync(AppPaths paths, LogService log, Action<bool, string> check)
    {
        const string broken = "not a sqlite file";
        await File.WriteAllTextAsync(paths.DatabaseFile, broken);
        await File.WriteAllTextAsync(paths.DatabaseFile + "-wal", "saved wal material");
        await File.WriteAllTextAsync(paths.DatabaseFile + "-shm", "saved shm material");
        var database = new DatabaseService(paths, log);
        await database.InitializeAsync();
        var backup = database.RecoveryBackupPath;
        check(backup is not null && Directory.Exists(backup), "corrupt database recovers without a pooled Windows file lock");
        check(await File.ReadAllTextAsync(Path.Combine(backup!, "mianmian.db")) == broken,
            "recovery preserves the original corrupt database");
        check(await File.ReadAllTextAsync(Path.Combine(backup!, "mianmian.db-wal")) == "saved wal material" &&
            await File.ReadAllTextAsync(Path.Combine(backup!, "mianmian.db-shm")) == "saved shm material",
            "recovery preserves WAL and SHM alongside the database");
        await database.AddExchangeAsync(ChatMessage.User("new"), ChatMessage.Assistant("recovered"));
        check((await database.GetRecentMessagesAsync(2)).Last().Content == "recovered", "recovered database supports normal exchanges");
    }

    private static async Task TestLegacyMigrationAsync(string output, Action<bool, string> check)
    {
        var paths = new AppPaths(Path.GetFullPath(Path.Combine(output, Guid.NewGuid().ToString("N"))));
        await using (var connection = OpenConnection(paths))
        {
            await connection.OpenAsync();
            await using var seed = connection.CreateCommand();
            seed.CommandText = """
                CREATE TABLE conversations(id INTEGER PRIMARY KEY AUTOINCREMENT, role TEXT NOT NULL,
                    content TEXT NOT NULL, created_at TEXT NOT NULL, status TEXT NOT NULL);
                INSERT INTO conversations(role, content, created_at, status) VALUES
                    ('user', 'legacy orphan', '2026-01-01T00:00:00Z', 'complete'),
                    ('user', 'paired', '2026-01-01T00:00:01Z', 'complete'),
                    ('assistant', 'paired reply', '2026-01-01T00:00:02Z', 'complete');
                """;
            await seed.ExecuteNonQueryAsync();
        }
        var database = new DatabaseService(paths, new LogService(paths));
        await database.InitializeAsync();
        var history = await database.GetRecentMessagesAsync(20);
        check(history.Count == 2 && history[0].Content == "paired", "legacy orphan repair preserves complete exchanges");
        await using (var connection = OpenConnection(paths))
        {
            await connection.OpenAsync();
            await using var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            check(Convert.ToInt32(await version.ExecuteScalarAsync()) == 1, "legacy repair records the migration version");
        }
        await database.AddMessageAsync(ChatMessage.User("later orphan"));
        await database.InitializeAsync();
        check((await database.GetRecentMessagesAsync(20)).Any(message => message.Content == "later orphan"),
            "completed legacy migration is not repeated on every startup");
    }

    private static async Task TestCelebrationsAsync(string output, Action<bool, string> check)
    {
        var paths = new AppPaths(Path.GetFullPath(Path.Combine(output, Guid.NewGuid().ToString("N"))));
        var log = new LogService(paths);
        var database = new DatabaseService(paths, log);
        await database.InitializeAsync();
        var service = new CelebrationService(database, paths, log);
        var date = new DateTime(2026, 11, 7);
        await File.WriteAllTextAsync(paths.CelebrationsFile,
            "[{\"id\":\"anniversary\",\"calendar\":\"solar\",\"month\":11,\"day\":7,\"message\":\"hello\"}]");
        check((await service.GetPendingAsync(date))?.Message == "hello", "valid celebration configuration is loaded");
        await File.WriteAllTextAsync(paths.CelebrationsFile, "[\n {\"id\":");
        check((await service.GetPendingAsync(date))?.Message == "hello" &&
            service.LastConfigurationError?.Contains("行 2") == true,
            "an incomplete edit keeps the last valid celebrations and reports the JSON line");
        await File.WriteAllTextAsync(paths.CelebrationsFile,
            "[{\"id\":\"bad\",\"month\":13,\"day\":1,\"message\":\"hello\"}]");
        check((await service.GetPendingAsync(date))?.Message == "hello" &&
            service.LastConfigurationError?.Contains("第 1 项的 month") == true,
            "celebration validation identifies the item and field without losing valid rules");
        await File.WriteAllTextAsync(paths.CelebrationsFile, "[]");
        check(await service.GetPendingAsync(date) is null && service.LastConfigurationError is null,
            "a corrected celebration configuration replaces the previous rules");
        await File.WriteAllTextAsync(paths.CelebrationsFile,
            "[{\"id\":\"bad\",\"month\":2,\"day\":30,\"message\":\"hello\"}]");
        try
        {
            await new CelebrationService(database, paths).GetPendingAsync(date);
            check(false, "invalid first celebration configuration is rejected");
        }
        catch (InvalidDataException ex)
        {
            check(ex.Message.Contains("第 1 项的 day"), "invalid first celebration configuration has an actionable field error");
        }
    }

    private static async Task TestStreamingAsync(string output, Action<bool, string> check)
    {
        var paths = new AppPaths(Path.GetFullPath(Path.Combine(output, Guid.NewGuid().ToString("N"))));
        var log = new LogService(paths);
        await File.WriteAllTextAsync(paths.ConfigFile, "{\"ai\":{\"apiKey\":\"test-stream-only\"}}");
        await LocalConfig.LoadAsync(paths, log);
        var settings = new SettingsService(paths, log);
        const string content = "data: {\"choices\":[{\"delta\":{\"content\":\"first half\"},\"finish_reason\":null}]}\n\n";
        const string stop = "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        async Task<string> Read(string response)
        {
            using var handler = new StreamingHandler(response);
            using var http = new HttpClient(handler);
            var client = new DeepSeekClient(http, settings, new PetPersona("test"));
            var result = "";
            await foreach (var delta in client.StreamReplyAsync([ChatMessage.User("hello")], CancellationToken.None)) result += delta;
            return result;
        }
        check(await Read(": keep-alive\n\ndata: \n\ndata: {\"type\":\"ping\"}\n\n" + content +
            "data: {\"choices\":[],\"usage\":{\"total_tokens\":2}}\n\n" + stop) == "first half",
            "SSE comments, empty heartbeats, ping and usage metadata preserve valid replies");
        foreach (var invalid in new[]
        {
            "data: {this is broken json\n\n", "data: {\"error\":{\"message\":\"provider error\"}}\n\n",
            "event: error\n\n", "data: {\"choices\":[{\"delta\":{\"content\":123}}]}\n\n",
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":123}]}\n\n"
        })
        {
            var rejected = false;
            try { await Read(content + invalid + stop); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "damaged content or provider error cannot be marked as a complete reply");
        }
        var interrupted = false;
        try { await Read(content); }
        catch (InvalidDataException) { interrupted = true; }
        check(interrupted, "a disconnected stream without a completion marker is rejected");
        await File.WriteAllTextAsync(paths.ConfigFile, "{}");
        await LocalConfig.LoadAsync(paths, log);
    }

    private static SqliteConnection OpenConnection(AppPaths paths) => new(new SqliteConnectionStringBuilder
    {
        DataSource = paths.DatabaseFile,
        Pooling = false
    }.ToString());

    private sealed class StreamingHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
    }
}

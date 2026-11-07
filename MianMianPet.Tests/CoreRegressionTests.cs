using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Win32;
using MianMianPet.Configuration;
using MianMianPet.Infrastructure;
using MianMianPet.Models;
using MianMianPet.Services;
using MianMianPet.ViewModels;

namespace MianMianPet.Tests;

internal static class CoreRegressionTests
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        var paths = new AppPaths(Path.GetFullPath(Path.Combine(output, Guid.NewGuid().ToString("N"))));
        var log = new LogService(paths);
        var registryPath = @"Software\MianMianPet.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            await StreamingSettingsAsync(paths, log, registryPath, check);
            await PersonaRepliesAsync(paths, log, check);
            await LoggingAsync(paths, check);
            await ActivationAsync(paths, check);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryPath, false);
            await File.WriteAllTextAsync(paths.ConfigFile, "{}");
            await LocalConfig.LoadAsync(paths, log);
        }
    }

    private static async Task StreamingSettingsAsync(AppPaths paths, LogService log, string registryPath, Action<bool, string> check)
    {
        await File.WriteAllTextAsync(paths.ConfigFile,
            "{\"ai\":{\"apiKey\":\"test-only-core-credential\",\"bubbleAutoHideSeconds\":9}}");
        await LocalConfig.LoadAsync(paths, log);
        var database = new DatabaseService(paths, log);
        await database.InitializeAsync();
        var settings = new SettingsService(paths, log);
        await settings.LoadAsync();
        var startup = new StartupService(registryPath, "Run", Environment.ProcessPath!);
        var conversation = new ConversationService(database);

        using (var handler = new GatedReplyHandler("第一段，", "第二段。"))
        using (var http = new HttpClient(handler))
        using (var pet = CreatePet(http, settings, conversation, log, database, paths))
        {
            try
            {
                pet.InputText = "ordinary settings during streaming";
                pet.SendCommand.Execute(null);
                await WaitUntilAsync(() => pet.BubbleText == "第一段，" && pet.IsBusy);
                var ordinary = new SettingsViewModel(settings, startup, conversation) { AlwaysOnTop = false };
                ordinary.SettingsSaved += (_, _) => pet.NotifySettingsChanged(ordinary.IdentityChangedOnSave);
                await ordinary.SaveAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(pet.IsBusy && pet.IsBubbleOpen && pet.BubbleText == "第一段，" && !ordinary.IdentityChangedOnSave,
                    "real streaming reply survives an ordinary settings save and application notification");
                handler.Release();
                await WaitUntilAsync(() => !pet.IsBusy);
                var history = await database.GetRecentMessagesAsync(16);
                check(history.Count == 2 && history[1].Content == "第一段，第二段。" && pet.BubbleText == history[1].Content,
                    "both streamed chunks are displayed and atomically stored after ordinary settings save");
                var timer = (DispatcherTimer)typeof(PetViewModel).GetField("_bubbleTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(pet)!;
                check(timer.IsEnabled && timer.Interval.TotalSeconds >= 9,
                    "configured nine-second minimum reaches the actual completed-reply timer");
            }
            finally { handler.Release(); }
        }

        using (var handler = new GatedReplyHandler("旧身份第一段，", "旧身份第二段。"))
        using (var http = new HttpClient(handler))
        using (var pet = CreatePet(http, settings, conversation, log, database, paths))
        {
            Task? pendingRename = null;
            try
            {
                pet.InputText = "rename during streaming";
                pet.SendCommand.Execute(null);
                await WaitUntilAsync(() => pet.BubbleText == "旧身份第一段，" && pet.IsBusy);
                var rename = new SettingsViewModel(settings, startup, conversation) { PetName = "云朵", UserTitle = "朋友" };
                rename.SettingsSaved += (_, _) => pet.NotifySettingsChanged(rename.IdentityChangedOnSave);
                pendingRename = rename.SaveAsync();
                await Task.Delay(75);
                check(!pendingRename.IsCompleted && rename.IsWorking && settings.Current.PetName == "绵绵" &&
                    pet.BubbleText == "旧身份第一段，", "identity save waits on the real active SendAsync lease");
                handler.Release();
                await pendingRename.WaitAsync(TimeSpan.FromSeconds(5));
                await WaitUntilAsync(() => !pet.IsBusy);
                var history = await database.GetRecentMessagesAsync(16);
                check(history.Count == 4 && history[^1].Content == "旧身份第一段，旧身份第二段。" &&
                    handler.Persona == "你是绵绵，正在陪小静。",
                    "old-identity stream finishes completely before the identity switch");
                var context = await conversation.BuildContextAsync(ChatMessage.User("new context"));
                check(settings.Current.PetName == "云朵" && settings.Current.UserTitle == "朋友" &&
                    context.Count == 1 && context[0].Content == "new context" && !pet.IsBubbleOpen,
                    "new identity excludes late old replies without deleting their complete stored exchange");
            }
            finally
            {
                handler.Release();
                if (pendingRename is not null) await pendingRename.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static PetViewModel CreatePet(HttpClient http, SettingsService settings, ConversationService conversation,
        LogService log, DatabaseService database, AppPaths paths) =>
        new(new DeepSeekClient(http, settings, new PetPersona("你是{PetName}，正在陪{UserTitle}。")),
            conversation, settings, log, new CelebrationService(database, paths));

    private static async Task PersonaRepliesAsync(AppPaths paths, LogService log, Action<bool, string> check)
    {
        var defaults = new PetExperienceConfig();
        var observed = Enumerable.Range(0, 40).Select(seed => defaults.GetCompanionReply(new Random(seed),
            new DateTime(2026, 11, 7, 12, 0, 0), "朋友", "云朵", null)).ToArray();
        check(observed.Any(reply => reply.Contains("云朵", StringComparison.Ordinal)) &&
            observed.All(reply => !reply.Contains("{PetName}", StringComparison.Ordinal) && !reply.Contains("绵绵有偷偷看到", StringComparison.Ordinal)),
            "built-in companion replies use the renamed pet identity");

        const string custom = "绵绵是我特意写在这句自定义文案里的名字。";
        var configuration = JsonSerializer.Serialize(new
        {
            experience = new
            {
                networkFailureReplies = new[] { "云朵把绵绵的信号挡住啦，等等它飘过去吧。" },
                dayCompanionReplies = new[] { custom, "{PetName}陪{UserTitle}。" },
                serviceFailureReplies = new[] { "{PetName}陪着{UserTitle}，稍后再试。" }
            }
        });
        await File.WriteAllTextAsync(paths.ConfigFile, configuration);
        await LocalConfig.LoadAsync(paths, log);
        check(LocalConfig.Current.Experience.NetworkFailureReplies[0] == "云朵把{PetName}的信号挡住啦，等等它飘过去吧。" &&
            LocalConfig.Current.Experience.DayCompanionReplies[0] == custom && await File.ReadAllTextAsync(paths.ConfigFile) == configuration,
            "exact legacy built-in reply upgrades in memory while custom text and configuration file stay intact");
        check(LocalConfig.Current.Experience.GetCompanionReply(new Random(1), new DateTime(2026, 11, 7, 12, 0, 0),
            "朋友", "云朵", custom) == "云朵陪朋友。", "custom companion placeholders render the current identity");

        var settings = new SettingsService(paths, log);
        await settings.LoadAsync();
        var database = new DatabaseService(paths, log);
        await database.InitializeAsync();
        using var handler = new GatedReplyHandler("unused", "unused");
        using var http = new HttpClient(handler);
        using var pet = CreatePet(http, settings, new ConversationService(database), log, database, paths);
        pet.InputText = "missing-key failure";
        pet.SendCommand.Execute(null);
        await WaitUntilAsync(() => pet.SendCommand.CanExecute(null));
        check(pet.BubbleText == "云朵陪着朋友，稍后再试。" && handler.Requests == 0,
            "offline failure reply formats both pet and user placeholders without an HTTP request");
    }

    private static async Task LoggingAsync(AppPaths paths, Action<bool, string> check)
    {
        const string credential = "test-only-log-credential";
        await File.WriteAllTextAsync(paths.ConfigFile, "{\"ai\":{\"apiKey\":\"" + credential + "\"}}");
        var log = new LogService(paths);
        await LocalConfig.LoadAsync(paths, log);
        await log.ErrorAsync("core_nested_error", NestedException(credential));
        var file = Path.Combine(paths.Logs, $"mianmian-{DateTime.Now:yyyyMMdd}.log");
        var text = await File.ReadAllTextAsync(file);
        check(text.Contains("System.IO.IOException", StringComparison.Ordinal) &&
            text.Contains("System.InvalidOperationException", StringComparison.Ordinal) && text.Contains(nameof(NestedException), StringComparison.Ordinal) &&
            text.Contains("[REDACTED]", StringComparison.Ordinal) && !text.Contains(credential, StringComparison.Ordinal),
            "error diagnostics retain exception types, inner cause and stack while removing credentials");

        var old = Path.Combine(paths.Logs, "mianmian-old-test.log");
        await File.WriteAllTextAsync(old, "expired");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-31));
        var unrelated = Path.Combine(paths.Logs, "unrelated.txt");
        await File.WriteAllTextAsync(unrelated, "keep");
        for (var i = 0; i < 6; i++)
        {
            using var large = File.Create(Path.Combine(paths.Logs, $"mianmian-size-test-{i}.log"));
            large.SetLength(LogService.MaximumFileBytes);
        }
        await new LogService(paths).InfoAsync("cleanup_trigger");
        check(!File.Exists(old) && Directory.GetFiles(paths.Logs, "mianmian-*.log").Sum(path => new FileInfo(path).Length) <= LogService.MaximumTotalBytes &&
            File.Exists(unrelated), "log cleanup enforces retention and total size without deleting unrelated files");
        using (var current = File.Open(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            current.SetLength(LogService.MaximumFileBytes);
        await new LogService(paths).InfoAsync("rotation_trigger");
        check(new FileInfo(file).Length < LogService.MaximumFileBytes &&
            Directory.GetFiles(paths.Logs, $"mianmian-{DateTime.Now:yyyyMMdd}-*.log").Length > 0,
            "full daily log rotates before appending a new event");
    }

    private static Exception NestedException(string credential)
    {
        try
        {
            try { throw new InvalidOperationException("inner credential=" + credential); }
            catch (Exception inner) { throw new IOException("outer diagnostic", inner); }
        }
        catch (Exception exception) { return exception; }
    }

    private static async Task ActivationAsync(AppPaths paths, Action<bool, string> check)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var log = new LogService(paths);
        using var primary = new SingleInstanceGuard(log, suffix, TimeSpan.FromMilliseconds(200));
        using var duplicate = new SingleInstanceGuard(log, suffix, TimeSpan.FromMilliseconds(200));
        using var isolated = new SingleInstanceGuard(log, suffix + "-other", TimeSpan.FromMilliseconds(200));
        check(primary.IsPrimary && !duplicate.IsPrimary && isolated.IsPrimary && primary.PipeName != isolated.PipeName,
            "single-instance mutex and pipe share a consistent isolated scope");
        var activated = 0;
        var isolatedActivated = 0;
        primary.ActivationRequested += (_, _) => activated++;
        isolated.ActivationRequested += (_, _) => isolatedActivated++;
        primary.StartListening();
        isolated.StartListening();
        using (var silent = new NamedPipeClientStream(".", primary.PipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await silent.ConnectAsync(timeout.Token);
            await Task.Delay(450);
            await SendActivationAsync(primary.PipeName, "SHOW\n");
            await WaitUntilAsync(() => activated == 1);
            check(isolatedActivated == 0, "a silent connected client times out and cannot block a subsequent activation");
        }
        await SendActivationAsync(primary.PipeName, new string('X', 40) + "\nSHOW\n");
        await Task.Delay(100);
        check(activated == 1, "an oversized activation command is ignored without accepting a trailing SHOW");
        await SendActivationAsync(primary.PipeName, "SHOW\n");
        await WaitUntilAsync(() => activated == 2);
        await SendActivationAsync(isolated.PipeName, "SHOW\n");
        await WaitUntilAsync(() => isolatedActivated == 1);
        check(activated == 2, "oversized clients do not stop listening and isolated activations stay in their scope");
    }

    private static async Task SendActivationAsync(string pipeName, string command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(timeout.Token);
        await client.WriteAsync(Encoding.UTF8.GetBytes(command), timeout.Token);
        await client.FlushAsync(timeout.Token);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Core regression transition timed out.");
            await Task.Delay(15);
        }
    }

    private sealed class GatedReplyHandler(string first, string second) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Requests { get; private set; }
        internal string? Persona { get; private set; }
        internal void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Persona = json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new GatedReplyStream(Event(first), Event(second) +
                    "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n", _release.Task))
            };
        }

        private static string Event(string content) => "data: " + JsonSerializer.Serialize(new
        {
            choices = new[] { new { delta = new { content } } }
        }) + "\n\n";
    }

    private sealed class GatedReplyStream(string first, string second, Task release) : Stream
    {
        private readonly byte[] _first = Encoding.UTF8.GetBytes(first);
        private readonly byte[] _second = Encoding.UTF8.GetBytes(second);
        private int _offset;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset < _first.Length)
            {
                var count = Math.Min(buffer.Length, _first.Length - _offset);
                _first.AsMemory(_offset, count).CopyTo(buffer);
                _offset += count;
                return count;
            }
            await release.WaitAsync(cancellationToken);
            var secondOffset = _offset - _first.Length;
            var remaining = Math.Min(buffer.Length, _second.Length - secondOffset);
            _second.AsMemory(secondOffset, remaining).CopyTo(buffer);
            _offset += remaining;
            return remaining;
        }
    }
}

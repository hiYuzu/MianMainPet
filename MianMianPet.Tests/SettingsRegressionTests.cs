using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using MianMianPet.Infrastructure;
using MianMianPet.Models;
using MianMianPet.Services;
using MianMianPet.ViewModels;
using MianMianPet.Views;

namespace MianMianPet.Tests;

internal static class SettingsRegressionTests
{
    internal static async Task RunAsync(string output, Action<bool, string> check)
    {
        var paths = new AppPaths(Path.GetFullPath(Path.Combine(output, Guid.NewGuid().ToString("N"))));
        var log = new LogService(paths);
        var database = new DatabaseService(paths, log);
        await database.InitializeAsync();
        var conversation = new ConversationService(database);
        var settings = new SettingsService(paths, log);
        await settings.LoadAsync();
        var registryPath = @"Software\MianMianPet.Tests\" + Guid.NewGuid().ToString("N");
        var startup = new StartupService(registryPath, "Run", Environment.ProcessPath!);
        try
        {
            var nameRejected = false;
            var titleRejected = false;
            try { new AppSettings { PetName = new string('羊', AppSettings.MaximumNameLength + 1) }.Normalize(); }
            catch (InvalidDataException) { nameRejected = true; }
            try { new AppSettings { UserTitle = new string('友', AppSettings.MaximumTitleLength + 1) }.Normalize(); }
            catch (InvalidDataException) { titleRejected = true; }
            check(nameRejected && titleRejected, "long pet names and user titles fail before saving");
            var toolTip = TrayService.CreateToolTip(new string('羊', 200));
            check(toolTip.Length <= 63 && TrayService.CreateToolTip("棉花") == "棉花 AI 桌宠",
                "tray tooltip supports current identity and safely bounds legacy long names");

            using (var key = Registry.CurrentUser.CreateSubKey(registryPath))
                key.SetValue("Run", $"\"{Path.Combine(paths.Root, "missing.exe")}\" --old", RegistryValueKind.String);
            check(!startup.IsEnabled(), "missing startup target is not reported as enabled");
            var oldExecutable = Path.Combine(paths.Root, "old.exe");
            await File.WriteAllTextAsync(oldExecutable, "old test executable");
            using (var key = Registry.CurrentUser.CreateSubKey(registryPath))
                key.SetValue("Run", $"\"{oldExecutable}\" --old", RegistryValueKind.String);
            check(!startup.IsEnabled(), "another existing executable is not the current startup target");
            startup.SetEnabled(true);
            check(startup.IsEnabled(), "current executable is recognized as the startup target");

            const string originalCommand = "\"%TEMP%\\mianmian-old.exe\" --keep-this-argument";
            using (var key = Registry.CurrentUser.CreateSubKey(registryPath))
                key.SetValue("Run", originalCommand, RegistryValueKind.ExpandString);
            var failed = false;
            using (var locked = new FileStream(paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { await settings.UpdateStartWithWindowsAsync(true, startup); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            }
            var restored = startup.Capture();
            check(failed && restored.Exists && restored.Kind == RegistryValueKind.ExpandString &&
                (string?)restored.Value == originalCommand && !settings.Current.StartWithWindows,
                "failed settings write restores exact startup command, arguments and registry type");

            startup.SetEnabled(false);
            await conversation.SaveExchangeAsync(ChatMessage.User("old identity"), ChatMessage.Assistant("old reply"));
            var viewModel = new SettingsViewModel(settings, startup, conversation) { PetName = "棉花" };
            await settings.UpdateStartWithWindowsAsync(true, startup);
            await viewModel.SaveAsync();
            check(startup.IsEnabled() && settings.Current.StartWithWindows && viewModel.StartWithWindows,
                "saving an unchanged startup field preserves the newer tray choice");
            check(viewModel.IdentityChangedOnSave && (await conversation.BuildContextAsync(ChatMessage.User("new identity"))).Count == 1,
                "identity save resets context after previous exchanges finish");

            viewModel = new SettingsViewModel(settings, startup, conversation) { PetName = "云朵" };
            await conversation.ContextGate.WaitAsync();
            Task pendingSave;
            var closed = false;
            var window = new SettingsWindow(viewModel);
            window.Closed += (_, _) => closed = true;
            try
            {
                pendingSave = viewModel.SaveAsync();
                check(viewModel.IsWorking && !pendingSave.IsCompleted && settings.Current.PetName == "棉花",
                    "identity save waits for the active conversation without changing settings");
                window.Close();
                check(!closed, "settings window cannot close while an identity save is waiting");
                await conversation.SaveExchangeAsync(ChatMessage.User("late old user"), ChatMessage.Assistant("late old reply"));
            }
            finally { conversation.ContextGate.Release(); }
            await pendingSave;
            window.Close();
            check(closed && settings.Current.PetName == "云朵" &&
                (await conversation.BuildContextAsync(ChatMessage.User("after rename"))).Count == 1,
                "late old-identity replies stay outside the renamed context and closing works after saving");

            var boundary = await conversation.GetContextStartIdAsync();
            viewModel = new SettingsViewModel(settings, startup, conversation) { AlwaysOnTop = false };
            await conversation.ContextGate.WaitAsync();
            try
            {
                await viewModel.SaveAsync().WaitAsync(TimeSpan.FromSeconds(3));
                check(!viewModel.IdentityChangedOnSave && !settings.Current.AlwaysOnTop &&
                    await conversation.GetContextStartIdAsync() == boundary,
                    "ordinary settings save does not wait for or reset the active conversation");
            }
            finally { conversation.ContextGate.Release(); }

            var beforeFailure = await File.ReadAllTextAsync(paths.SettingsFile);
            viewModel = new SettingsViewModel(settings, startup, conversation) { PetName = "待重试" };
            await ExecuteAsync(paths, "ALTER TABLE app_state RENAME TO app_state_test_backup;");
            failed = false;
            try
            {
                try { await viewModel.SaveAsync(); }
                catch (SqliteException) { failed = true; }
                check(failed && settings.Current.PetName == "云朵" && await File.ReadAllTextAsync(paths.SettingsFile) == beforeFailure,
                    "failed context reset leaves identity settings unchanged");
            }
            finally { await ExecuteAsync(paths, "ALTER TABLE app_state_test_backup RENAME TO app_state;"); }
            await viewModel.SaveAsync();
            check(viewModel.IdentityChangedOnSave && settings.Current.PetName == "待重试",
                "retry still performs the identity context reset");

            await conversation.SaveExchangeAsync(ChatMessage.User("kept user"), ChatMessage.Assistant("kept reply"));
            boundary = await conversation.GetContextStartIdAsync();
            viewModel = new SettingsViewModel(settings, startup, conversation) { PetName = "保存失败" };
            failed = false;
            using (var locked = new FileStream(paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { await viewModel.SaveAsync(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            }
            check(failed && settings.Current.PetName == "待重试" && await conversation.GetContextStartIdAsync() == boundary &&
                (await conversation.BuildContextAsync(ChatMessage.User("still here"))).Count == 3,
                "failed identity file save restores the previous conversation boundary");

            viewModel = new SettingsViewModel(settings, startup, conversation) { AlwaysOnTop = true };
            await Task.WhenAll(viewModel.SaveAsync(), settings.UpdateStartWithWindowsAsync(true, startup));
            check(settings.Current.StartWithWindows && startup.IsEnabled() && settings.Current.AlwaysOnTop,
                "overlapping tray and settings writes preserve both choices");

            const string damagedSettings = "{\"PetName\":";
            await File.WriteAllTextAsync(paths.SettingsFile, damagedSettings);
            settings = new SettingsService(paths, log);
            await settings.LoadAsync();
            var backups = Directory.GetFiles(paths.Root, "settings-invalid-*.json");
            using var recoveredJson = JsonDocument.Parse(await File.ReadAllTextAsync(paths.SettingsFile));
            check(backups.Length == 1 && await File.ReadAllTextAsync(backups[0]) == damagedSettings &&
                settings.Current.PetName == "绵绵" && settings.ContextResetRequired && settings.RecoveryNotice is not null,
                "damaged settings keep a unique original backup and durable pending context recovery");
            settings = new SettingsService(paths, log);
            await settings.LoadAsync();
            check(settings.ContextResetRequired, "pending identity recovery survives a new settings instance");
            await ExecuteAsync(paths, "ALTER TABLE app_state RENAME TO app_state_test_backup;");
            failed = false;
            try
            {
                try { await settings.CompleteRecoveryAsync(conversation); }
                catch (SqliteException) { failed = true; }
                check(failed && settings.ContextResetRequired, "failed recovery retains its pending marker for retry");
            }
            finally { await ExecuteAsync(paths, "ALTER TABLE app_state_test_backup RENAME TO app_state;"); }
            await settings.CompleteRecoveryAsync(conversation);
            check(!settings.ContextResetRequired &&
                (await conversation.BuildContextAsync(ChatMessage.User("default identity"))).Count == 1,
                "completed settings recovery excludes all previous identity history");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); }
    }

    private static async Task ExecuteAsync(AppPaths paths, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={paths.DatabaseFile}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

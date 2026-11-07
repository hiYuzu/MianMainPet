using System.Text.Json;
using System.Text.Json.Serialization;
using MianMianPet.Infrastructure;
using MianMianPet.Models;

namespace MianMianPet.Services;

public sealed class SettingsService
{
    private readonly AppPaths _paths;
    private readonly LogService _logService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Current { get; private set; } = new();
    public string? RecoveryNotice { get; private set; }
    public bool ContextResetRequired => File.Exists(RecoveryMarker);
    private string RecoveryMarker => Path.Combine(_paths.Root, "settings.context-reset-required");

    public SettingsService(AppPaths paths, LogService logService)
    {
        _paths = paths;
        _logService = logService;
    }

    public async Task<AppSettings> LoadAsync()
    {
        RecoveryNotice = ContextResetRequired ? "设置恢复尚未完成，本次会重新开始聊天上下文。" : null;
        if (!File.Exists(_paths.SettingsFile))
        {
            Current = new AppSettings();
            await SaveAsync(Current);
            return Current;
        }

        try
        {
            await using var stream = File.OpenRead(_paths.SettingsFile);
            Current = (await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions)
                ?? throw new InvalidDataException("设置文件应为 JSON 对象。"))
                .Normalize();
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            await _logService.ErrorAsync("settings_load_failed", ex);
            var backup = Path.Combine(_paths.Root, $"settings-invalid-{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
            File.Copy(_paths.SettingsFile, backup);
            await File.WriteAllTextAsync(RecoveryMarker, "reset conversation after settings recovery");
            Current = new AppSettings();
            await SaveAsync(Current);
            RecoveryNotice = $"设置文件有误，已恢复默认设置并重新开始聊天。原文件已保留在：\n{backup}";
        }

        return Current;
    }

    internal async Task CompleteRecoveryAsync(ConversationService conversation)
    {
        if (!ContextResetRequired) return;
        await conversation.ContextGate.WaitAsync();
        try
        {
            await conversation.ResetContextAsync();
            File.Delete(RecoveryMarker);
        }
        finally
        {
            conversation.ContextGate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _gate.WaitAsync();
        try
        {
            await SaveCoreAsync(settings.Normalize());
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task SaveWithStartupAsync(AppSettings settings, StartupService startup, bool applyStartupChange)
    {
        await _gate.WaitAsync();
        try
        {
            var normalized = settings.Normalize();
            normalized.StartWithWindows = applyStartupChange ? normalized.StartWithWindows : startup.IsEnabled();
            await SaveWithStartupCoreAsync(normalized, startup, applyStartupChange);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task UpdateStartWithWindowsAsync(bool enabled, StartupService startup)
    {
        await _gate.WaitAsync();
        try
        {
            var updated = new AppSettings
            {
                PetName = Current.PetName,
                UserTitle = Current.UserTitle,
                StartWithWindows = enabled,
                AlwaysOnTop = Current.AlwaysOnTop
            };
            await SaveWithStartupCoreAsync(updated, startup, applyStartupChange: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveWithStartupCoreAsync(AppSettings normalized, StartupService startup, bool applyStartupChange)
    {
        var snapshot = applyStartupChange ? startup.Capture() : null;
        try
        {
            if (applyStartupChange) startup.SetEnabled(normalized.StartWithWindows);
            await SaveCoreAsync(normalized);
        }
        catch (Exception saveError)
        {
            if (snapshot is not null)
            {
                try { startup.Restore(snapshot); }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("设置保存失败，开机启动的原始状态也未能恢复。", saveError, rollbackError);
                }
            }
            throw;
        }
    }

    private async Task SaveCoreAsync(AppSettings normalized)
    {
        var tempFile = _paths.SettingsFile + ".tmp";
        await using (var stream = File.Create(tempFile))
        {
            await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions);
        }

        File.Move(tempFile, _paths.SettingsFile, true);
        Current = normalized;
    }
}

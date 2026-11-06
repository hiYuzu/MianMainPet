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

    public SettingsService(AppPaths paths, LogService logService)
    {
        _paths = paths;
        _logService = logService;
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_paths.SettingsFile))
        {
            Current = new AppSettings();
            await SaveAsync(Current);
            return Current;
        }

        try
        {
            await using var stream = File.OpenRead(_paths.SettingsFile);
            Current = (await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings())
                .Normalize();
        }
        catch (Exception ex)
        {
            await _logService.ErrorAsync("settings_load_failed", ex);
            Current = new AppSettings();
        }

        return Current;
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

    public async Task UpdateStartWithWindowsAsync(bool enabled)
    {
        await _gate.WaitAsync();
        try
        {
            var current = Current;
            var updated = new AppSettings
            {
                PetName = current.PetName,
                UserTitle = current.UserTitle,
                StartWithWindows = enabled,
                AlwaysOnTop = current.AlwaysOnTop
            };
            await SaveCoreAsync(updated.Normalize());
        }
        finally
        {
            _gate.Release();
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

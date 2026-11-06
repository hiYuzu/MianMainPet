using MianMianPet.Configuration;
using MianMianPet.Infrastructure;

namespace MianMianPet.Services;

public sealed class LogService
{
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LogService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task InfoAsync(string eventName) => await WriteAsync("INFO", eventName);

    public async Task ErrorAsync(string eventName, Exception exception) =>
        await WriteAsync("ERROR", $"{eventName}: {Sanitize(exception.Message)}");

    private async Task WriteAsync(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {Sanitize(message)}{Environment.NewLine}";
        var file = Path.Combine(_paths.Logs, $"mianmian-{DateTime.Now:yyyyMMdd}.log");

        await _gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(file, line);
        }
        catch
        {
            // Logging must never terminate the desktop pet.
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Sanitize(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ');
        const string bearer = "Bearer ";
        var index = singleLine.IndexOf(bearer, StringComparison.OrdinalIgnoreCase);
        var sanitized = index < 0 ? singleLine : singleLine[..index] + "Bearer [REDACTED]";
        return LocalConfig.Current.Ai.IsConfigured
            ? sanitized.Replace(LocalConfig.Current.Ai.ApiKey, "[REDACTED]", StringComparison.Ordinal)
            : sanitized;
    }
}

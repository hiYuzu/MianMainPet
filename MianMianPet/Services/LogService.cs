using MianMianPet.Configuration;
using MianMianPet.Infrastructure;
using System.Text;

namespace MianMianPet.Services;

public sealed class LogService
{
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal const long MaximumFileBytes = 2 * 1024 * 1024;
    internal const long MaximumTotalBytes = 10 * 1024 * 1024;
    private DateTime _lastCleanupDate;

    public LogService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task InfoAsync(string eventName) => await WriteAsync("INFO", eventName);

    public async Task ErrorAsync(string eventName, Exception exception) =>
        await WriteAsync("ERROR", $"{eventName}: {exception}");

    private async Task WriteAsync(string level, string message)
    {
        var sanitized = Sanitize(message);
        if (sanitized.Length > 16000) sanitized = sanitized[..16000] + " [TRUNCATED]";
        var line = $"{DateTimeOffset.Now:O} [{level}] {sanitized}{Environment.NewLine}";
        var file = Path.Combine(_paths.Logs, $"mianmian-{DateTime.Now:yyyyMMdd}.log");

        await _gate.WaitAsync();
        try
        {
            var rotated = false;
            if (File.Exists(file) && new FileInfo(file).Length + Encoding.UTF8.GetByteCount(line) > MaximumFileBytes)
            {
                File.Move(file, Path.Combine(_paths.Logs, $"mianmian-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log"));
                rotated = true;
            }
            await File.AppendAllTextAsync(file, line);
            if (rotated || _lastCleanupDate != DateTime.Today)
            {
                CleanupLogs();
                _lastCleanupDate = DateTime.Today;
            }
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

    private void CleanupLogs()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        long total = 0;
        foreach (var log in new DirectoryInfo(_paths.Logs).GetFiles("mianmian-*.log")
                     .OrderByDescending(item => item.LastWriteTimeUtc))
        {
            if (log.LastWriteTimeUtc < cutoff || total + log.Length > MaximumTotalBytes)
                log.Delete();
            else
                total += log.Length;
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

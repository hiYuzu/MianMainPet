using Microsoft.Win32;

namespace MianMianPet.Services;

public sealed class StartupService
{
    private readonly string _runKey;
    private readonly string _valueName;
    private readonly string _executablePath;

    public StartupService() : this(@"Software\Microsoft\Windows\CurrentVersion\Run", "MianMianPet",
        Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。")) { }

    internal StartupService(string runKey, string valueName, string executablePath)
    {
        _runKey = runKey;
        _valueName = valueName;
        _executablePath = Path.GetFullPath(executablePath);
    }

    public bool IsEnabled()
    {
        var snapshot = Capture();
        if (snapshot.Value is not string command || string.IsNullOrWhiteSpace(command)) return false;
        command = Environment.ExpandEnvironmentVariables(command).Trim();
        string executable;
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end < 0) return false;
            executable = command[1..end];
        }
        else
        {
            var end = command.IndexOfAny([' ', '\t']);
            executable = end < 0 ? command : command[..end];
        }

        try
        {
            return Path.IsPathFullyQualified(executable) && File.Exists(executable) &&
                string.Equals(Path.GetFullPath(executable), _executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey, true)
            ?? Registry.CurrentUser.CreateSubKey(_runKey, true);
        if (enabled)
        {
            key.SetValue(_valueName, $"\"{_executablePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(_valueName, false);
        }
    }

    internal StartupValueSnapshot Capture()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey, false);
        if (key is null || !key.GetValueNames().Contains(_valueName, StringComparer.OrdinalIgnoreCase))
            return new StartupValueSnapshot(false, null, RegistryValueKind.Unknown);
        return new StartupValueSnapshot(true, key.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames),
            key.GetValueKind(_valueName));
    }

    internal void Restore(StartupValueSnapshot snapshot)
    {
        if (!snapshot.Exists)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(_runKey, true);
            existing?.DeleteValue(_valueName, false);
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(_runKey, true);
        key.SetValue(_valueName, snapshot.Value!, snapshot.Kind);
    }
}

internal sealed record StartupValueSnapshot(bool Exists, object? Value, RegistryValueKind Kind);

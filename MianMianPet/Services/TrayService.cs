using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace MianMianPet.Services;

public sealed class TrayService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly Action _showPet;
    private readonly Action _showSettings;
    private readonly Action _showAbout;
    private readonly Action _exit;
    private readonly Icon _appIcon;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _startupItem;

    public TrayService(
        SettingsService settingsService,
        StartupService startupService,
        Action showPet,
        Action showSettings,
        Action showAbout,
        Action exit)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _showPet = showPet;
        _showSettings = showSettings;
        _showAbout = showAbout;
        _exit = exit;

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("设置", null, (_, _) => Dispatch(_showSettings));
        _startupItem = new WinForms.ToolStripMenuItem("开机启动")
        {
            CheckOnClick = true,
            Checked = _startupService.IsEnabled()
        };
        _startupItem.Click += async (_, _) => await ToggleStartupAsync();
        menu.Items.Add(_startupItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("关于", null, (_, _) => Dispatch(_showAbout));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatch(_exit));

        _appIcon = LoadApplicationIcon();
        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = CreateToolTip(_settingsService.Current.PetName),
            Icon = _appIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatch(_showPet);
    }

    public void Refresh()
    {
        _notifyIcon.Text = CreateToolTip(_settingsService.Current.PetName);
        _startupItem.Checked = _startupService.IsEnabled();
    }

    internal static string CreateToolTip(string petName)
    {
        var text = $"{petName} AI 桌宠";
        const int maximumLength = 63;
        if (text.Length <= maximumLength) return text;
        var length = char.IsHighSurrogate(text[maximumLength - 1]) ? maximumLength - 1 : maximumLength;
        return text[..length];
    }

    private async Task ToggleStartupAsync()
    {
        var requestedEnabled = _startupItem.Checked;
        _startupItem.Enabled = false;
        try
        {
            await _settingsService.UpdateStartWithWindowsAsync(requestedEnabled, _startupService);
            _startupItem.Checked = _startupService.IsEnabled();
        }
        catch (Exception ex)
        {
            var actualEnabled = _settingsService.Current.StartWithWindows;
            try
            {
                actualEnabled = _startupService.IsEnabled();
            }
            catch
            {
                // 无法读取注册表时，至少让界面回到操作前的已知状态。
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _startupItem.Checked = actualEnabled;
                MessageBox.Show(ex.Message, "开机启动设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
        finally
        {
            _startupItem.Enabled = true;
        }
    }

    private static void Dispatch(Action action) => Application.Current.Dispatcher.Invoke(action);

    private static Icon LoadApplicationIcon()
    {
        var resource = Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/Images/MianMianPet.ico", UriKind.Absolute));
        if (resource is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var icon = new Icon(resource.Stream);
        return (Icon)icon.Clone();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _appIcon.Dispose();
    }
}

using System.Windows;
using MianMianPet.Configuration;
using MianMianPet.Infrastructure;
using MianMianPet.Models;
using MianMianPet.Services;
using MianMianPet.ViewModels;
using MianMianPet.Views;

namespace MianMianPet;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private HttpClient? _httpClient;
    private TrayService? _trayService;
    private PetWindow? _petWindow;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private PetViewModel? _petViewModel;
    private SettingsService? _settingsService;
    private StartupService? _startupService;
    private ConversationService? _conversationService;
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var paths = new AppPaths();
            var logService = new LogService(paths);
            _singleInstance = new SingleInstanceGuard(logService);
            if (!_singleInstance.IsPrimary)
            {
                await SingleInstanceGuard.NotifyPrimaryAsync(logService);
                Shutdown();
                return;
            }
            await LocalConfig.LoadAsync(paths, logService);
            _settingsService = new SettingsService(paths, logService);
            await _settingsService.LoadAsync();
            _startupService = new StartupService();
            var database = new DatabaseService(paths, logService);
            await database.InitializeAsync();
            _conversationService = new ConversationService(database);
            await _settingsService.CompleteRecoveryAsync(_conversationService);

            var persona = new PetPersona(LocalConfig.Current.PersonaPrompt);
            _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MianMianPet/2.2.0");
            var deepSeekClient = new DeepSeekClient(_httpClient, _settingsService, persona);
            _petViewModel = new PetViewModel(
                deepSeekClient,
                _conversationService,
                _settingsService,
                logService,
                new CelebrationService(database, paths, logService));
            await _petViewModel.InitializeAsync();
            _petWindow = new PetWindow(
                _petViewModel,
                database,
                _settingsService,
                logService,
                ShowSettings,
                ExitApplication);
            _petWindow.Show();

            _trayService = new TrayService(
                _settingsService,
                _startupService,
                ShowPet,
                ShowSettings,
                ShowAbout,
                ExitApplication);

            _singleInstance.ActivationRequested += (_, _) => Dispatcher.Invoke(ShowPet);
            _singleInstance.StartListening();
            await logService.InfoAsync("application_started");
            var recoveryNotice = _settingsService.RecoveryNotice;
            if (database.RecoveryBackupPath is { } backupPath)
                recoveryNotice = $"{recoveryNotice}\n聊天数据库已重建，旧文件保留在：\n{backupPath}".Trim();
            if (!string.IsNullOrWhiteSpace(recoveryNotice))
                MessageBox.Show(recoveryNotice, "绵绵已恢复本地数据", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"绵绵启动失败：{ex.Message}",
                "绵绵 AI 桌宠",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    public void ShowSettings()
    {
        _petWindow?.RegisterExternalInteraction();
        if (_settingsService is null || _startupService is null || _conversationService is null)
        {
            return;
        }

        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(
            _settingsService,
            _startupService,
            _conversationService);
        viewModel.SettingsSaved += (_, _) =>
        {
            _petWindow?.ApplySettings();
            _petViewModel?.NotifySettingsChanged(viewModel.IdentityChangedOnSave);
            _trayService?.Refresh();
        };
        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowPet()
    {
        if (_petWindow is null)
        {
            return;
        }

        _petWindow.Show();
        _petWindow.Activate();
        _petWindow.Topmost = _settingsService?.Current.AlwaysOnTop ?? true;
        _petWindow.RegisterExternalInteraction();
    }

    public void ShowAbout()
    {
        _petWindow?.RegisterExternalInteraction();
        if (_aboutWindow is { IsVisible: true })
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    public void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _petWindow?.AllowClose();
        _aboutWindow?.Close();
        _settingsWindow?.Close();
        _petWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _petViewModel?.Dispose();
        _httpClient?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

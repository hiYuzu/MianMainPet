using System.Windows;
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
        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.IsPrimary)
        {
            await SingleInstanceGuard.NotifyPrimaryAsync();
            Shutdown();
            return;
        }

        try
        {
            var paths = new AppPaths();
            var logService = new LogService(paths);
            _settingsService = new SettingsService(paths, logService);
            await _settingsService.LoadAsync();
            _startupService = new StartupService();
            var database = new DatabaseService(paths, logService);
            await database.InitializeAsync();
            _conversationService = new ConversationService(database);

            await using var promptStream = typeof(App).Assembly.GetManifestResourceStream(
                "MianMianPet.Resources.persona-prompt.txt")
                ?? throw new InvalidOperationException("内置人设提示词资源缺失。 ");
            using var promptReader = new StreamReader(promptStream);
            var prompt = await promptReader.ReadToEndAsync();
            var persona = new PetPersona(prompt);
            _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MianMianPet/2.0.4");
            var deepSeekClient = new DeepSeekClient(_httpClient, _settingsService, persona);
            _petViewModel = new PetViewModel(
                deepSeekClient,
                _conversationService,
                _settingsService,
                logService);
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
            _petViewModel?.NotifySettingsChanged();
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

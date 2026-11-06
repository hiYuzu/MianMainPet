using MianMianPet.Infrastructure;
using MianMianPet.Models;
using MianMianPet.Services;

namespace MianMianPet.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly ConversationService _conversationService;
    private string _petName;
    private string _userTitle;
    private bool _startWithWindows;
    private bool _alwaysOnTop;
    private string _statusMessage = string.Empty;
    private bool _isWorking;

    public SettingsViewModel(
        SettingsService settingsService,
        StartupService startupService,
        ConversationService conversationService)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _conversationService = conversationService;
        var settings = settingsService.Current;
        _petName = settings.PetName;
        _userTitle = settings.UserTitle;
        _startWithWindows = startupService.IsEnabled();
        _alwaysOnTop = settings.AlwaysOnTop;
    }

    public string PetName { get => _petName; set => SetProperty(ref _petName, value); }
    public string UserTitle { get => _userTitle; set => SetProperty(ref _userTitle, value); }
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }
    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); }
    public bool IsWorking { get => _isWorking; private set => SetProperty(ref _isWorking, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public event EventHandler? SettingsSaved;

    public async Task SaveAsync()
    {
        if (IsWorking)
        {
            return;
        }

        IsWorking = true;
        StatusMessage = "正在保存…";
        try
        {
            var previous = _settingsService.Current;
            var updated = new AppSettings
            {
                PetName = PetName,
                UserTitle = UserTitle,
                StartWithWindows = StartWithWindows,
                AlwaysOnTop = AlwaysOnTop
            }.Normalize();
            var identityChanged =
                !string.Equals(previous.PetName, updated.PetName, StringComparison.Ordinal) ||
                !string.Equals(previous.UserTitle, updated.UserTitle, StringComparison.Ordinal);

            var previousRegistryEnabled = _startupService.IsEnabled();
            try
            {
                _startupService.SetEnabled(updated.StartWithWindows);
                await _settingsService.SaveAsync(updated);
            }
            catch
            {
                try
                {
                    _startupService.SetEnabled(previousRegistryEnabled);
                }
                catch
                {
                    // 保留最初的保存异常，由设置窗口展示给用户。
                }

                throw;
            }

            if (identityChanged)
            {
                await _conversationService.ResetContextAsync();
            }

            StatusMessage = "已保存。";
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsWorking = false;
        }
    }
}

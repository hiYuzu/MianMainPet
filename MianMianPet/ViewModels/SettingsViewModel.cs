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
    private bool _initialStartWithWindows;

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
        _initialStartWithWindows = _startWithWindows;
        _alwaysOnTop = settings.AlwaysOnTop;
    }

    public string PetName { get => _petName; set => SetProperty(ref _petName, value); }
    public string UserTitle { get => _userTitle; set => SetProperty(ref _userTitle, value); }
    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }
    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); }
    public bool IsWorking { get => _isWorking; private set => SetProperty(ref _isWorking, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IdentityChangedOnSave { get; private set; }

    public event EventHandler? SettingsSaved;

    public async Task SaveAsync()
    {
        if (IsWorking)
        {
            return;
        }

        IsWorking = true;
        IdentityChangedOnSave = false;
        StatusMessage = "正在保存…";
        var contextLocked = false;
        var contextReset = false;
        var settingsSaved = false;
        long previousContextStart = 0;
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

            if (identityChanged)
            {
                StatusMessage = "正在等待这次聊天结束…";
                await _conversationService.ContextGate.WaitAsync();
                contextLocked = true;
                previousContextStart = await _conversationService.GetContextStartIdAsync();
                await _conversationService.ResetContextAsync();
                contextReset = true;
            }

            StatusMessage = "正在保存…";
            await _settingsService.SaveWithStartupAsync(updated, _startupService, StartWithWindows != _initialStartWithWindows);
            settingsSaved = true;
            IdentityChangedOnSave = identityChanged;
            _initialStartWithWindows = _settingsService.Current.StartWithWindows;
            StartWithWindows = _initialStartWithWindows;

            StatusMessage = "已保存。";
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception saveError)
        {
            if (contextReset && !settingsSaved)
            {
                try { await _conversationService.RestoreContextAsync(previousContextStart); }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("设置未保存，聊天上下文的恢复也未完成。", saveError, rollbackError);
                }
            }
            throw;
        }
        finally
        {
            if (contextLocked) _conversationService.ContextGate.Release();
            IsWorking = false;
        }
    }
}

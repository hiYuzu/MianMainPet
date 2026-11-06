using System.Text;
using System.Windows.Threading;
using MianMianPet.Animation;
using MianMianPet.Configuration;
using MianMianPet.Infrastructure;
using MianMianPet.Models;
using MianMianPet.Services;

namespace MianMianPet.ViewModels;

public sealed class PetViewModel : ObservableObject, IDisposable
{
    private readonly DeepSeekClient _deepSeekClient;
    private readonly ConversationService _conversationService;
    private readonly SettingsService _settingsService;
    private readonly LogService _logService;
    private readonly CelebrationService _celebrationService;
    private readonly DispatcherTimer _flushTimer;
    private readonly DispatcherTimer _bubbleTimer;
    private readonly StringBuilder _pendingText = new();
    private readonly Random _random = new();
    private CancellationTokenSource? _requestCancellation;
    private string _inputText = string.Empty;
    private string _bubbleText = string.Empty;
    private bool _isInputOpen;
    private bool _isBubbleOpen;
    private bool _isBusy;
    private bool _isThinking;
    private bool _isHoverHintVisible;
    private bool _isBubbleHovered;
    private Celebration? _pendingCelebration;
    private bool _checkingCelebration;
    private bool _openingGift;
    private int _bubbleAutoHideSeconds = InternalAiConfig.BubbleAutoHideSeconds;

    public PetViewModel(
        DeepSeekClient deepSeekClient,
        ConversationService conversationService,
        SettingsService settingsService,
        LogService logService,
        CelebrationService celebrationService)
    {
        _deepSeekClient = deepSeekClient;
        _conversationService = conversationService;
        _settingsService = settingsService;
        _logService = logService;
        _celebrationService = celebrationService;
        OpenGiftCommand = new AsyncRelayCommand(OpenGiftAsync, () => HasGift, OnCommandError);
        SendCommand = new AsyncRelayCommand(SendAsync, CanSend, OnCommandError);
        ToggleInputCommand = new RelayCommand(ToggleInput, () => !IsBusy);
        HideInputCommand = new RelayCommand(HideInput);
        _flushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background, FlushPendingText, Dispatcher.CurrentDispatcher);
        _flushTimer.Stop();
        _bubbleTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher);
        _bubbleTimer.Tick += (_, _) =>
        {
            _bubbleTimer.Stop();
            IsBubbleOpen = false;
        };
    }

    public string PetName => _settingsService.Current.PetName;
    public string InputPlaceholder => $"跟{PetName}说点什么…";
    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string BubbleText
    {
        get => _bubbleText;
        private set
        {
            if (SetProperty(ref _bubbleText, value)) OnPropertyChanged(nameof(BubbleWidth));
        }
    }
    public double BubbleWidth
    {
        get
        {
            if (IsThinking) return 200;
            if (BubbleText.Length <= 18) return 230;
            return BubbleText.Length <= 60 ? 300 : 370;
        }
    }
    public bool IsInputOpen
    {
        get => _isInputOpen;
        private set
        {
            if (SetProperty(ref _isInputOpen, value)) RefreshGiftVisibility();
        }
    }
    public bool IsBubbleOpen { get => _isBubbleOpen; private set => SetProperty(ref _isBubbleOpen, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SendCommand.RaiseCanExecuteChanged();
                ToggleInputCommand.RaiseCanExecuteChanged();
                RefreshGiftVisibility();
            }
        }
    }

    public bool IsThinking
    {
        get => _isThinking;
        private set
        {
            if (SetProperty(ref _isThinking, value)) OnPropertyChanged(nameof(BubbleWidth));
        }
    }
    public bool HasGift => _pendingCelebration is not null && !IsBusy && !IsInputOpen && !_openingGift;
    public AsyncRelayCommand OpenGiftCommand { get; }
    public bool IsHoverHintVisible { get => _isHoverHintVisible; set => SetProperty(ref _isHoverHintVisible, value); }
    public AsyncRelayCommand SendCommand { get; }
    public RelayCommand ToggleInputCommand { get; }
    public RelayCommand HideInputCommand { get; }

    public event EventHandler? FocusInputRequested;
    public event EventHandler<PetState>? StateRequested;

    public async Task InitializeAsync()
    {
        BubbleText = await _conversationService.GetLastAssistantReplyAsync() ?? string.Empty;
        await CheckCelebrationAsync();
    }

    public async Task CheckCelebrationAsync()
    {
        if (_checkingCelebration || _openingGift) return;
        _checkingCelebration = true;
        try
        {
            var date = DateTime.Now.Date;
            var pending = await _celebrationService.GetPendingAsync(date);
            if (!_openingGift && date == DateTime.Now.Date)
            {
                _pendingCelebration = pending;
                RefreshGiftVisibility();
            }
        }
        catch (Exception ex)
        {
            _pendingCelebration = null;
            RefreshGiftVisibility();
            await _logService.ErrorAsync("celebration_check_failed", ex);
        }
        finally
        {
            _checkingCelebration = false;
        }
    }

    private async Task OpenGiftAsync()
    {
        var gift = _pendingCelebration;
        if (!HasGift || gift is null) return;
        _openingGift = true;
        _pendingCelebration = null;
        RefreshGiftVisibility();
        try
        {
            gift = await _celebrationService.GetPendingAsync(DateTime.Now.Date);
            if (gift is null) return;
            if (IsBusy || IsInputOpen)
            {
                _pendingCelebration = gift;
                return;
            }
            _bubbleTimer.Stop();
            IsThinking = false;
            IsHoverHintVisible = false;
            BubbleText = gift.Message
                .Replace("{UserTitle}", _settingsService.Current.UserTitle, StringComparison.Ordinal)
                .Replace("{PetName}", PetName, StringComparison.Ordinal);
            IsBubbleOpen = true;
            StateRequested?.Invoke(this, PetState.Petted);
            ScheduleBubbleAutoHide(20);
            await _celebrationService.MarkOpenedAsync(gift);
        }
        catch (Exception ex)
        {
            await _logService.ErrorAsync("celebration_save_failed", ex);
        }
        finally
        {
            _openingGift = false;
            RefreshGiftVisibility();
        }
    }

    private void RefreshGiftVisibility()
    {
        OnPropertyChanged(nameof(HasGift));
        OpenGiftCommand.RaiseCanExecuteChanged();
    }

    public void ToggleInput()
    {
        if (IsBusy)
        {
            return;
        }

        IsInputOpen = !IsInputOpen;
        IsHoverHintVisible = false;
        if (IsInputOpen)
        {
            IsBubbleOpen = false;
            StateRequested?.Invoke(this, PetState.Listening);
            FocusInputRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (!IsBusy)
        {
            StateRequested?.Invoke(this, PetState.Idle);
            if (!string.IsNullOrWhiteSpace(BubbleText))
            {
                IsBubbleOpen = true;
                ScheduleReadingTime();
            }
        }
    }

    public void HideInput()
    {
        IsInputOpen = false;
        if (!IsBusy)
        {
            StateRequested?.Invoke(this, PetState.Idle);
        }
    }

    public void PauseBubbleAutoHide()
    {
        _isBubbleHovered = true;
        _bubbleTimer.Stop();
    }

    public void ResumeBubbleAutoHide()
    {
        _isBubbleHovered = false;
        if (!IsBusy && IsBubbleOpen)
        {
            ScheduleBubbleAutoHide(_bubbleAutoHideSeconds);
        }
    }

    public bool TryShowCompanionMessage(string message)
    {
        if (IsBusy || IsInputOpen || IsBubbleOpen || string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        _bubbleTimer.Stop();
        IsThinking = false;
        BubbleText = message;
        IsBubbleOpen = true;
        ScheduleBubbleAutoHide(PetExperienceConfig.CompanionBubbleAutoHideSeconds);
        return true;
    }

    public void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(PetName));
        OnPropertyChanged(nameof(InputPlaceholder));
        BubbleText = string.Empty;
        IsBubbleOpen = false;
        _isBubbleHovered = false;
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText) && InputText.Length <= 2000;

    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (text.Length == 0 || IsBusy)
        {
            return;
        }

        if (!InternalAiConfig.IsConfigured)
        {
            BubbleText = PickFailureReply(PetExperienceConfig.ServiceFailureReplies);
            IsBubbleOpen = true;
            IsInputOpen = false;
            StateRequested?.Invoke(this, PetState.Error);
            await Task.Delay(550);
            StateRequested?.Invoke(this, PetState.Idle);
            ScheduleBubbleAutoHide(PetExperienceConfig.FailureBubbleAutoHideSeconds);
            return;
        }

        var userMessage = ChatMessage.User(text);
        var receivedAnyContent = false;
        var requestAttempted = false;
        var exchangeSaved = false;

        try
        {
            _bubbleTimer.Stop();
            _requestCancellation = new CancellationTokenSource(PetExperienceConfig.AiRequestTimeout);
            IsBusy = true;
            IsThinking = true;
            IsHoverHintVisible = false;
            IsInputOpen = false;
            IsBubbleOpen = true;
            BubbleText = string.Empty;
            _pendingText.Clear();
            StateRequested?.Invoke(this, PetState.Thinking);
            _flushTimer.Start();
            requestAttempted = true;

            var context = await _conversationService.BuildContextAsync(userMessage);
            await foreach (var delta in _deepSeekClient.StreamReplyAsync(context, _requestCancellation.Token))
            {
                if (!receivedAnyContent)
                {
                    receivedAnyContent = true;
                    IsThinking = false;
                    StateRequested?.Invoke(this, PetState.Speaking);
                }

                _pendingText.Append(delta);
            }

            FlushPendingText(this, EventArgs.Empty);
            if (string.IsNullOrWhiteSpace(BubbleText))
            {
                throw new InvalidOperationException("这次没有收到回复，请再试一次。 ");
            }

            await _conversationService.SaveExchangeAsync(
                userMessage,
                ChatMessage.Assistant(BubbleText));
            exchangeSaved = true;
            InputText = string.Empty;
            await _logService.InfoAsync("chat_completed");
            StateRequested?.Invoke(this, PetState.Idle);
            ScheduleReadingTime();
        }
        catch (OperationCanceledException)
        {
            IsThinking = false;
            _pendingText.Clear();
            if (requestAttempted && !exchangeSaved)
            {
                await SaveUnsuccessfulMessageAsync(
                    userMessage,
                    MessageStatus.Failed);
            }

            BubbleText = PickFailureReply(PetExperienceConfig.TimeoutFailureReplies);
            await _logService.InfoAsync("chat_timed_out");
            StateRequested?.Invoke(this, PetState.Error);
            await Task.Delay(550);
            StateRequested?.Invoke(this, PetState.Idle);
            ScheduleBubbleAutoHide(PetExperienceConfig.FailureBubbleAutoHideSeconds);
        }

        catch (Exception ex)
        {
            _pendingText.Clear();
            IsThinking = false;
            if (exchangeSaved)
            {
                await _logService.ErrorAsync("chat_finalize_failed", ex);
                return;
            }

            if (requestAttempted && !exchangeSaved)
            {
                await SaveUnsuccessfulMessageAsync(userMessage, MessageStatus.Failed);
            }

            BubbleText = GetFriendlyMessage(ex);
            await _logService.ErrorAsync("chat_failed", ex);
            StateRequested?.Invoke(this, PetState.Error);
            await Task.Delay(550);
            StateRequested?.Invoke(this, PetState.Idle);
            ScheduleBubbleAutoHide(PetExperienceConfig.FailureBubbleAutoHideSeconds);
        }
        finally
        {
            _flushTimer.Stop();
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            IsThinking = false;
            IsBusy = false;
        }
    }

    private string GetFriendlyMessage(Exception exception) => exception switch
    {
        DeepSeekApiException => PickFailureReply(PetExperienceConfig.ServiceFailureReplies),
        HttpRequestException => PickFailureReply(PetExperienceConfig.NetworkFailureReplies),
        TaskCanceledException => PickFailureReply(PetExperienceConfig.TimeoutFailureReplies),
        _ => PickFailureReply(PetExperienceConfig.GeneralFailureReplies)
    };

    private string PickFailureReply(IReadOnlyList<string> replies) =>
        PetExperienceConfig.PickReply(_random, replies);

    private async Task SaveUnsuccessfulMessageAsync(ChatMessage userMessage, MessageStatus status)
    {
        try
        {
            await _conversationService.SaveAsync(userMessage with { Status = status });
        }
        catch (Exception ex)
        {
            await _logService.ErrorAsync("chat_outcome_save_failed", ex);
        }
    }

    private void FlushPendingText(object? sender, EventArgs e)
    {
        if (_pendingText.Length == 0)
        {
            return;
        }

        BubbleText += _pendingText.ToString();
        _pendingText.Clear();
    }

    private void ScheduleBubbleAutoHide(int seconds = InternalAiConfig.BubbleAutoHideSeconds)
    {
        _bubbleTimer.Stop();
        _bubbleAutoHideSeconds = seconds;
        if (_isBubbleHovered)
        {
            return;
        }

        _bubbleTimer.Interval = TimeSpan.FromSeconds(seconds);
        _bubbleTimer.Start();
    }

    private void ScheduleReadingTime() =>
        ScheduleBubbleAutoHide(Math.Clamp(2 + BubbleText.Length / 12, 4, 20));

    private void OnCommandError(Exception exception)
    {
        BubbleText = GetFriendlyMessage(exception);
        IsBubbleOpen = true;
        ScheduleBubbleAutoHide(PetExperienceConfig.FailureBubbleAutoHideSeconds);
    }

    public void Dispose()
    {
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _flushTimer.Stop();
        _bubbleTimer.Stop();
    }

}

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
    private bool _cancelRequested;
    private int _bubbleAutoHideSeconds = InternalAiConfig.BubbleAutoHideSeconds;

    public PetViewModel(
        DeepSeekClient deepSeekClient,
        ConversationService conversationService,
        SettingsService settingsService,
        LogService logService)
    {
        _deepSeekClient = deepSeekClient;
        _conversationService = conversationService;
        _settingsService = settingsService;
        _logService = logService;
        SendCommand = new AsyncRelayCommand(SendAsync, CanSend, OnCommandError);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
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

    public string BubbleText { get => _bubbleText; private set => SetProperty(ref _bubbleText, value); }
    public bool IsInputOpen { get => _isInputOpen; private set => SetProperty(ref _isInputOpen, value); }
    public bool IsBubbleOpen { get => _isBubbleOpen; private set => SetProperty(ref _isBubbleOpen, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SendCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
                ToggleInputCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsThinking { get => _isThinking; private set => SetProperty(ref _isThinking, value); }
    public bool IsHoverHintVisible { get => _isHoverHintVisible; set => SetProperty(ref _isHoverHintVisible, value); }
    public AsyncRelayCommand SendCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ToggleInputCommand { get; }
    public RelayCommand HideInputCommand { get; }

    public event EventHandler? FocusInputRequested;
    public event EventHandler<PetState>? StateRequested;

    public async Task InitializeAsync()
    {
        BubbleText = await _conversationService.GetLastAssistantReplyAsync() ?? string.Empty;
    }

    public void ToggleInput()
    {
        if (IsBusy)
        {
            return;
        }

        IsInputOpen = !IsInputOpen;
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
            _cancelRequested = false;
            _requestCancellation = new CancellationTokenSource(PetExperienceConfig.AiRequestTimeout);
            IsBusy = true;
            IsThinking = true;
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
            ScheduleBubbleAutoHide();
        }
        catch (OperationCanceledException)
        {
            _pendingText.Clear();
            if (requestAttempted && !exchangeSaved)
            {
                await SaveUnsuccessfulMessageAsync(
                    userMessage,
                    _cancelRequested ? MessageStatus.Cancelled : MessageStatus.Failed);
            }

            BubbleText = _cancelRequested
                ? "好呀，绵绵先停下来啦。"
                : PickFailureReply(PetExperienceConfig.TimeoutFailureReplies);
            await _logService.InfoAsync(_cancelRequested ? "chat_cancelled" : "chat_timed_out");
            if (_cancelRequested)
            {
                StateRequested?.Invoke(this, PetState.Idle);
                ScheduleBubbleAutoHide();
            }
            else
            {
                StateRequested?.Invoke(this, PetState.Error);
                await Task.Delay(550);
                StateRequested?.Invoke(this, PetState.Idle);
                ScheduleBubbleAutoHide(PetExperienceConfig.FailureBubbleAutoHideSeconds);
            }
        }
        catch (Exception ex)
        {
            _pendingText.Clear();
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

    private void Cancel()
    {
        _cancelRequested = true;
        _requestCancellation?.Cancel();
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

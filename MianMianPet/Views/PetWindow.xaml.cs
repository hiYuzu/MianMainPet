using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MianMianPet.Animation;
using MianMianPet.Configuration;
using MianMianPet.Infrastructure;
using MianMianPet.Services;
using MianMianPet.ViewModels;

namespace MianMianPet.Views;

public partial class PetWindow : Window
{
    private readonly PetViewModel _viewModel;
    private readonly DatabaseService _database;
    private readonly SettingsService _settingsService;
    private readonly LogService _logService;
    private readonly Action _showSettings;
    private readonly Action _exitApplication;
    private readonly PetStateController _stateController;
    private readonly DispatcherTimer _ambientTimer;
    private readonly DispatcherTimer _sleepTimer;
    private readonly DispatcherTimer _companionTimer;
    private readonly DispatcherTimer _dayPhaseTimer;
    private readonly Random _random = new();
    private InputWindow? _inputWindow;
    private Point _dragStartScreen;
    private Point _dragStartWindow;
    private bool _isPointerDown;
    private bool _didDrag;
    private bool _allowClose;
    private bool _hasShownCompanionMessage;
    private string? _lastCompanionReply;
    private Point? _lastPetPoint;
    private double _pettingDistance;
    private long _pettingStartedAt;
    private long _lastPettingAt;

    public PetWindow(
        PetViewModel viewModel,
        DatabaseService database,
        SettingsService settingsService,
        LogService logService,
        Action showSettings,
        Action exitApplication)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _database = database;
        _settingsService = settingsService;
        _logService = logService;
        _showSettings = showSettings;
        _exitApplication = exitApplication;
        DataContext = viewModel;
        _stateController = new PetStateController(PetArtwork);
        _ambientTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = LocalConfig.Current.Experience.AmbientIdleDelay
        };
        _ambientTimer.Tick += AmbientTimer_OnTick;
        _sleepTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = LocalConfig.Current.Experience.SleepIdleDelay
        };
        _sleepTimer.Tick += SleepTimer_OnTick;
        _companionTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _companionTimer.Tick += CompanionTimer_OnTick;
        _dayPhaseTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = LocalConfig.Current.Experience.DayPhaseRefreshInterval
        };
        _dayPhaseTimer.Tick += DayPhaseTimer_OnTick;
        _fullScreenTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _fullScreenTimer.Tick += FullScreenTimer_OnTick;
        InitializePlayTimers();
        _viewModel.FocusInputRequested += ViewModel_OnFocusInputRequested;
        _viewModel.StateRequested += ViewModel_OnStateRequested;
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        SystemEvents.DisplaySettingsChanged += SystemEvents_OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += SystemEvents_OnPowerModeChanged;
    }

    public void ApplySettings()
    {
        Topmost = _settingsService.Current.AlwaysOnTop;
        if (_inputWindow is not null)
        {
            _inputWindow.Topmost = Topmost;
        }
        SynchronizePopupZOrder();
        const double fixedPetSize = 150d;
        Width = fixedPetSize;
        Height = fixedPetSize;
        if (!double.IsNaN(Left) && !double.IsNaN(Top))
        {
            var clamped = ScreenPlacementHelper.ClampPetPosition(this, Left, Top);
            Left = clamped.X;
            Top = clamped.Y;
        }
    }

    public void AllowClose() => _allowClose = true;

    public void RegisterExternalInteraction()
    {
        CancelPlay();
        RegisterInteraction();
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshFullScreenSuppression();
        _fullScreenTimer.Start();
        if (_inputWindow is null)
        {
            _inputWindow = new InputWindow(_viewModel)
            {
                Owner = this,
                Topmost = Topmost
            };
            _inputWindow.SizeChanged += (_, _) => PositionInputWindow();
        }

        ApplySettings();
        try
        {
            var leftText = await _database.GetStateAsync("pet.left");
            var topText = await _database.GetStateAsync("pet.top");
            if (double.TryParse(leftText, NumberStyles.Float, CultureInfo.InvariantCulture, out var left) &&
                double.TryParse(topText, NumberStyles.Float, CultureInfo.InvariantCulture, out var top))
            {
                var position = ScreenPlacementHelper.ClampPetPosition(this, left, top);
                Left = position.X;
                Top = position.Y;
            }
            else
            {
                SetDefaultPosition();
            }
        }
        catch (Exception ex)
        {
            await _logService.ErrorAsync("pet_position_load_failed", ex);
            SetDefaultPosition();
        }

        if (IsVisible && !RefreshFullScreenSuppression())
        {
            UpdateDayPhase();
            _dayPhaseTimer.Start();
        }
        RestartIdleTimers();
        ScheduleNextCompanionMessage();
        Opacity = 1;
        await LoadPlayHistoryAsync();
    }

    private void Window_OnPreviewMouseDown(object sender, MouseButtonEventArgs e) => RegisterInteraction();

    private void Window_OnLocationChanged(object? sender, EventArgs e)
    {
        if (IsLoaded && !_isClosing) PositionInputWindow();
    }

    private void Window_OnActivationChanged(object? sender, EventArgs e)
    {
        if (IsLoaded && !_isClosing) SynchronizePopupZOrder();
    }

    private void SynchronizePopupZOrder()
    {
        PopupWindowHelper.SynchronizeZOrder(this, BubblePopup);
        PopupWindowHelper.SynchronizeZOrder(this, PlayPopup);
    }

    private void Pet_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _lastPetPoint = null;
        if (_stateController.CurrentState is PetState.Petted) _stateController.SetState(PetState.Idle);
        _isPointerDown = true;
        _didDrag = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartWindow = new Point(Left, Top);
        PetHitArea.CaptureMouse();
        e.Handled = true;
    }

    private void Pet_OnMouseMove(object sender, MouseEventArgs e)
    {
        RegisterInteraction();
        if (!_isPointerDown || e.LeftButton != MouseButtonState.Pressed)
        {
            if (_isPointerDown) CancelPointerInteraction();
            TrackPetting(e.GetPosition(this));
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        var deltaPixels = current - _dragStartScreen;
        var dpi = VisualTreeHelper.GetDpi(this);
        var delta = new Vector(deltaPixels.X / dpi.DpiScaleX, deltaPixels.Y / dpi.DpiScaleY);
        if (!_didDrag && Math.Abs(delta.X) + Math.Abs(delta.Y) < 5)
        {
            return;
        }

        _didDrag = true;
        _viewModel.HideInput();
        Left = _dragStartWindow.X + delta.X;
        Top = _dragStartWindow.Y + delta.Y;
        PositionInputWindow();
    }

    private async void Pet_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPointerDown)
        {
            return;
        }

        _isPointerDown = false;
        PetHitArea.ReleaseMouseCapture();
        if (_didDrag)
        {
            var clamped = ScreenPlacementHelper.ClampPetPosition(this, Left, Top);
            Left = clamped.X;
            Top = clamped.Y;
            PositionInputWindow();
            await SavePositionSafelyAsync();
            if (!_viewModel.IsBusy) _stateController.SetState(PetState.Idle);
        }
        else
        {
            _stateController.PlayClickReaction();
            _viewModel.ToggleInput();
        }

        e.Handled = true;
    }

    private void Pet_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isPointerDown) CancelPointerInteraction();
    }

    private void CancelPointerInteraction()
    {
        var didDrag = _didDrag;
        _isPointerDown = false;
        _didDrag = false;
        _lastPetPoint = null;
        PetHitArea.ReleaseMouseCapture();
        if (didDrag) EnsureValidPosition();
        if (!_viewModel.IsBusy && !_viewModel.IsInputOpen) _stateController.SetState(PetState.Idle);
        RegisterInteraction();
    }

    private void Pet_OnMouseEnter(object sender, MouseEventArgs e)
    {
        RegisterInteraction();
        if (!_viewModel.IsBusy && !_isPointerDown && !_viewModel.IsInputOpen)
        {
            _viewModel.IsHoverHintVisible = true;
            _stateController.SetState(PetState.Hovered);
        }
    }

    private void Pet_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _lastPetPoint = null;
        _viewModel.IsHoverHintVisible = false;
        if (!_viewModel.IsBusy && !_isPointerDown && !_viewModel.IsInputOpen &&
            _stateController.CurrentState is not PetState.Petted)
        {
            _stateController.SetState(PetState.Idle);
        }
    }

    private void TrackPetting(Point point)
    {
        var now = Environment.TickCount64;
        if (_viewModel.IsBusy || _viewModel.IsInputOpen || point.Y > Height * 0.65 ||
            _stateController.CurrentState is PetState.Petted || now - _lastPettingAt < 3000)
        {
            _lastPetPoint = null;
            return;
        }

        if (_lastPetPoint is null || now - _pettingStartedAt > 1500)
        {
            _pettingDistance = 0;
            _pettingStartedAt = now;
            _lastPetPoint = point;
            return;
        }

        var distance = (point - _lastPetPoint.Value).Length;
        _lastPetPoint = point;
        if (distance > 35)
        {
            _lastPetPoint = null;
            return;
        }

        _pettingDistance += distance;
        if (_pettingDistance >= 65 && now - _pettingStartedAt >= 250)
        {
            _lastPettingAt = now;
            _lastPetPoint = null;
            _viewModel.IsHoverHintVisible = false;
            _stateController.SetState(PetState.Petted);
        }
    }

    private void BubblePopup_OnOpened(object? sender, EventArgs e)
    {
        PlaceBubblePopup();
        PopupWindowHelper.SynchronizeZOrder(this, BubblePopup);
    }

    private void Bubble_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (BubblePopup.IsOpen)
        {
            PlaceBubblePopup();
        }
    }

    private void PlaceBubblePopup()
    {
        var workArea = ScreenPlacementHelper.GetWorkingAreaSize(this);
        BubbleControl.MaxWidth = Math.Max(120, workArea.Width - 24);
        var maximumHeight = Math.Max(120, workArea.Height - 24);
        BubbleControl.SetMaximumHeight(maximumHeight);
        var width = BubbleControl.ActualWidth > 0 ? BubbleControl.ActualWidth : BubbleControl.Width;
        var height = Math.Clamp(BubbleControl.ActualHeight, 80, maximumHeight);
        ScreenPlacementHelper.PlacePopup(this, BubblePopup, width, height);
        PopupWindowHelper.RefreshPosition(this, BubblePopup);
    }

    private void Bubble_OnMouseEnter(object sender, MouseEventArgs e)
    {
        RegisterInteraction();
        _viewModel.PauseBubbleAutoHide();
    }

    private void Bubble_OnMouseLeave(object sender, MouseEventArgs e)
    {
        RegisterInteraction();
        _viewModel.ResumeBubbleAutoHide();
    }

    private void ViewModel_OnFocusInputRequested(object? sender, EventArgs e)
    {
        RegisterInteraction();
        ShowInputWindow();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PetViewModel.IsBubbleOpen) or nameof(PetViewModel.HasGift))
            RefreshFullScreenSuppression();
        if (e.PropertyName is nameof(PetViewModel.InputText) or
            nameof(PetViewModel.IsInputOpen) or
            nameof(PetViewModel.IsBusy))
        {
            if (_play.IsActive && (_viewModel.IsInputOpen || _viewModel.IsBusy)) CancelPlay();
            RegisterInteraction();
        }

        if (e.PropertyName == nameof(PetViewModel.IsBubbleOpen) && _viewModel.IsBubbleOpen && _play.IsActive)
            CancelPlay();

        if (e.PropertyName != nameof(PetViewModel.IsInputOpen))
        {
            return;
        }

        if (_viewModel.IsInputOpen)
        {
            ShowInputWindow();
        }
        else
        {
            _inputWindow?.Hide();
        }
    }

    private void ShowInputWindow()
    {
        if (RefreshFullScreenSuppression() || _inputWindow is null)
        {
            return;
        }

        _inputWindow.Topmost = Topmost;
        PositionInputWindow();
        _inputWindow.ActivateInput();
        PositionInputWindow();
    }

    private void PositionInputWindow()
    {
        if (BubblePopup.IsOpen) PlaceBubblePopup();
        if (PlayPopup.IsOpen) PlayPopup_OnOpened(this, EventArgs.Empty);
        if (_inputWindow is not null && _viewModel.IsInputOpen)
        {
            _inputWindow.MaxWidth = Math.Max(120, ScreenPlacementHelper.GetWorkingAreaSize(this).Width - 16);
            ScreenPlacementHelper.PlaceFloatingWindow(this, _inputWindow);
        }
    }

    private void ViewModel_OnStateRequested(object? sender, PetState state) =>
        _stateController.SetState(state);

    private async Task SavePositionAsync()
    {
        await _database.SetStateAsync("pet.left", Left.ToString(CultureInfo.InvariantCulture));
        await _database.SetStateAsync("pet.top", Top.ToString(CultureInfo.InvariantCulture));
    }

    private void SettingsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        RegisterInteraction();
        _showSettings();
    }
    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e) => _exitApplication();

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _viewModel.FocusInputRequested -= ViewModel_OnFocusInputRequested;
        _fullScreenTimer.Stop();
        _fullScreenTimer.Tick -= FullScreenTimer_OnTick;
        DisposePlay();
        _viewModel.StateRequested -= ViewModel_OnStateRequested;
        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _ambientTimer.Stop();
        _ambientTimer.Tick -= AmbientTimer_OnTick;
        _sleepTimer.Stop();
        _sleepTimer.Tick -= SleepTimer_OnTick;
        _companionTimer.Stop();
        _companionTimer.Tick -= CompanionTimer_OnTick;
        _dayPhaseTimer.Stop();
        _dayPhaseTimer.Tick -= DayPhaseTimer_OnTick;
        _inputWindow?.AllowClose();
        _inputWindow?.Close();
        _inputWindow = null;
        SystemEvents.DisplaySettingsChanged -= SystemEvents_OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= SystemEvents_OnPowerModeChanged;
        _stateController.Dispose();
    }

    private void Window_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_isClosing) return;
        if (e.NewValue is false)
        {
            _stateController.SetSuspended(true);
            _viewModel.SetWindowHidden(true);
            _isPointerDown = false;
            _didDrag = false;
            _lastPetPoint = null;
            PetHitArea.ReleaseMouseCapture();
            _fullScreenTimer.Stop();
            CancelPlay(resumeIdle: false);
            _ambientTimer.Stop();
            _sleepTimer.Stop();
            _companionTimer.Stop();
            _dayPhaseTimer.Stop();
            PetArtwork.SetAmbientSceneVisible(false);
            _viewModel.HideInput();
            _inputWindow?.Hide();
        }
        else
        {
            _viewModel.SetWindowHidden(false);
            _fullScreenTimer.Start();
            if (RefreshFullScreenSuppression()) return;
            _stateController.SetSuspended(false);
            UpdateDayPhase();
            _dayPhaseTimer.Start();
            RestartIdleTimers();
            ScheduleNextCompanionMessage();
            ScheduleNextPlay();
        }
    }

    private void SystemEvents_OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            CancelPlay();
            RegisterInteraction();
            EnsureValidPosition();
        });

    private void SystemEvents_OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Dispatcher.InvokeAsync(() => CancelPlay(resumeIdle: false));
        }
        if (e.Mode == PowerModes.Resume)
        {
            Dispatcher.InvokeAsync(() =>
            {
                CancelPlay();
                RegisterInteraction();
                EnsureValidPosition();
            });
        }
    }

    private void AmbientTimer_OnTick(object? sender, EventArgs e)
    {
        if (RefreshFullScreenSuppression()) return;
        if (!IsVisible)
        {
            _ambientTimer.Stop();
            return;
        }

        if (_viewModel.IsBusy ||
            _viewModel.IsInputOpen ||
            PetHitArea.IsMouseOver ||
            BubbleControl.IsMouseOver ||
            _inputWindow?.IsMouseOver == true)
        {
            RestartAmbientTimer();
            return;
        }

        _ambientTimer.Stop();
        PetArtwork.SetAmbientSceneVisible(true);
    }

    private void SleepTimer_OnTick(object? sender, EventArgs e)
    {
        _sleepTimer.Stop();
        if (RefreshFullScreenSuppression()) return;
        if (!IsVisible)
        {
            return;
        }

        if (IsPetInteractionInProgress())
        {
            _sleepTimer.Interval = TimeSpan.FromSeconds(15);
            _sleepTimer.Start();
            return;
        }

        PetArtwork.SetAmbientSceneVisible(true);
        _stateController.SetState(PetState.Sleeping);
    }

    private void CompanionTimer_OnTick(object? sender, EventArgs e)
    {
        _companionTimer.Stop();
        if (RefreshFullScreenSuppression()) return;
        if (!IsVisible)
        {
            ScheduleNextCompanionMessage();
            return;
        }

        if (IsPetInteractionInProgress() ||
            _viewModel.IsBubbleOpen ||
            UserActivityHelper.GetSystemIdleTime() > LocalConfig.Current.Experience.MaximumSystemIdleForCompanion)
        {
            _companionTimer.Interval = LocalConfig.Current.Experience.CompanionBlockedRetryDelay;
            _companionTimer.Start();
            return;
        }

        var now = DateTime.Now;
        var reply = LocalConfig.Current.Experience.GetCompanionReply(
            _random,
            now,
            _settingsService.Current.UserTitle,
            _settingsService.Current.PetName,
            _lastCompanionReply);
        if (_viewModel.TryShowCompanionMessage(reply))
        {
            _hasShownCompanionMessage = true;
            _lastCompanionReply = reply;
            _sleepTimer.Stop();
            _sleepTimer.Interval = LocalConfig.Current.Experience.SleepIdleDelay;
            _sleepTimer.Start();
            _stateController.SetState(PetState.Idle);
        }

        ScheduleNextCompanionMessage();
    }

    private async void DayPhaseTimer_OnTick(object? sender, EventArgs e)
    {
        if (RefreshFullScreenSuppression()) return;
        UpdateDayPhase();
        await _viewModel.CheckCelebrationAsync();
    }

    private void RegisterInteraction()
    {
        if (_isFullScreenSuppressed) return;
        _playIdleSince = Environment.TickCount64;
        if (_play.IsActive) return;
        if (_stateController.CurrentState is PetState.Sleeping)
        {
            _stateController.SetState(PetState.Idle);
            _stateController.PlayWakeReaction();
        }

        PetArtwork.SetAmbientSceneVisible(false);
        RestartIdleTimers();
        ScheduleNextCompanionMessage();
    }

    private void RestartAmbientTimer()
    {
        _ambientTimer.Stop();
        if (IsLoaded && IsVisible && !_play.IsActive && !_isFullScreenSuppressed)
        {
            _ambientTimer.Interval = LocalConfig.Current.Experience.AmbientIdleDelay;
            _ambientTimer.Start();
        }
    }

    private void RestartIdleTimers()
    {
        RestartAmbientTimer();
        _sleepTimer.Stop();
        if (IsLoaded && IsVisible && !_play.IsActive && !_isFullScreenSuppressed)
        {
            _sleepTimer.Interval = LocalConfig.Current.Experience.SleepIdleDelay;
            _sleepTimer.Start();
        }
    }

    private void ScheduleNextCompanionMessage()
    {
        _companionTimer.Stop();
        if (!IsLoaded || !IsVisible || _play.IsActive || _isFullScreenSuppressed)
        {
            return;
        }

        _companionTimer.Interval = LocalConfig.Current.Experience.GetNextCompanionDelay(
            _random,
            DateTime.Now,
            isFirst: !_hasShownCompanionMessage);
        _companionTimer.Start();
    }

    private bool IsPetInteractionInProgress() =>
        _play.IsActive ||
        _viewModel.IsBusy ||
        _viewModel.IsInputOpen ||
        _isPointerDown ||
        Application.Current.Windows.OfType<SettingsWindow>().Any(window => window.IsVisible) ||
        PetHitArea.IsMouseOver ||
        BubbleControl.IsMouseOver ||
        _inputWindow?.IsMouseOver == true;

    private void UpdateDayPhase() =>
        PetArtwork.ApplyDayPhase(LocalConfig.Current.Experience.GetDayPhase(DateTime.Now));

    private async void EnsureValidPosition()
    {
        var clamped = ScreenPlacementHelper.ClampPetPosition(this, Left, Top);
        Left = clamped.X;
        Top = clamped.Y;
        PositionInputWindow();
        await SavePositionSafelyAsync();
    }

    private void SetDefaultPosition()
    {
        var position = ScreenPlacementHelper.GetDefaultPetPosition(this);
        Left = position.X;
        Top = position.Y;
    }

    private async Task SavePositionSafelyAsync()
    {
        try
        {
            await SavePositionAsync();
        }
        catch (Exception ex)
        {
            await _logService.ErrorAsync("pet_position_save_failed", ex);
        }
    }
}

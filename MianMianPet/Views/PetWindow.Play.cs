using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MianMianPet.Animation;
using MianMianPet.Configuration;
using MianMianPet.Infrastructure;
using MianMianPet.Models;

namespace MianMianPet.Views;

public partial class PetWindow
{
    private const int WalkExitMilliseconds = 1050;
    private readonly PetPlaySession _play = new();
    private DispatcherTimer _playTimer = null!;
    private DispatcherTimer _playFrameTimer = null!;
    private PetPlayKind? _lastPlayKind;
    private bool _playInitialized;
    private bool _hasPlayed;
    private bool _isClosing;
    private long _playIdleSince = Environment.TickCount64;

    private void InitializePlayTimers()
    {
        _playTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _playTimer.Tick += PlayTimer_OnTick;
        _playFrameTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _playFrameTimer.Tick += PlayFrameTimer_OnTick;
        PlayPanel.CloudChosen += PlayPanel_OnCloudChosen;
    }

    private async Task LoadPlayHistoryAsync()
    {
        try
        {
            var last = await _database.GetStateAsync("play.last");
            _lastPlayKind = last switch
            {
                "walk:returned" => PetPlayKind.Walk,
                "hide:found" or "hide:timeout" => PetPlayKind.HideAndSeek,
                _ => null
            };
        }
        catch (Exception ex)
        {
            await _logService.ErrorAsync("play_history_load_failed", ex);
        }
        _playInitialized = true;
        ScheduleNextPlay();
    }

    private void ScheduleNextPlay()
    {
        _playTimer.Stop();
        if (!_playInitialized || _isClosing || !IsLoaded || !IsVisible || _play.IsActive || _isFullScreenSuppressed) return;
        var minimum = _hasPlayed ? LocalConfig.Current.Experience.PlayMinimumDelay : LocalConfig.Current.Experience.PlayMinimumIdle;
        var maximum = _hasPlayed ? LocalConfig.Current.Experience.PlayMaximumDelay : LocalConfig.Current.Experience.FirstPlayMaximumDelay;
        _playTimer.Interval = minimum + TimeSpan.FromMilliseconds(_random.NextDouble() * (maximum - minimum).TotalMilliseconds);
        _playTimer.Start();
    }

    private void PlayTimer_OnTick(object? sender, EventArgs e)
    {
        _playTimer.Stop();
        if (RefreshFullScreenSuppression()) return;
        if (_isClosing || !IsVisible || !_playInitialized || _play.IsActive) return;
        if (!TryStartIdlePlay(UserActivityHelper.GetSystemIdleTime()))
        {
            _playTimer.Interval = LocalConfig.Current.Experience.PlayBlockedRetryDelay;
            _playTimer.Start();
        }
    }

    private bool TryStartIdlePlay(TimeSpan systemIdle)
    {
        if (RefreshFullScreenSuppression()) return false;
        if (Environment.TickCount64 - _playIdleSince < LocalConfig.Current.Experience.PlayMinimumIdle.TotalMilliseconds ||
            IsPetInteractionInProgress() || _viewModel.IsBubbleOpen || _viewModel.HasGift ||
            PetHitArea.ContextMenu.IsOpen ||
            Application.Current.Windows.OfType<AboutWindow>().Any(window => window.IsVisible) ||
            systemIdle > LocalConfig.Current.Experience.MaximumSystemIdleForCompanion) return false;

        var kind = _lastPlayKind switch
        {
            PetPlayKind.Walk => PetPlayKind.HideAndSeek,
            PetPlayKind.HideAndSeek => PetPlayKind.Walk,
            _ => _random.Next(2) == 0 ? PetPlayKind.Walk : PetPlayKind.HideAndSeek
        };
        StartPlay(kind);
        return true;
    }

    private void StartPlay(PetPlayKind kind)
    {
        if (RefreshFullScreenSuppression() || _play.IsActive) return;
        _hasPlayed = true;
        _ambientTimer.Stop();
        _sleepTimer.Stop();
        _companionTimer.Stop();
        _viewModel.IsHoverHintVisible = false;
        _stateController.SetState(PetState.Idle);
        PetArtwork.SetAmbientSceneVisible(false);
        var now = Environment.TickCount64;
        if (kind == PetPlayKind.Walk)
        {
            _play.StartWalk(WalkTreasures.All[_random.Next(WalkTreasures.All.Count)], now);
        }
        else
        {
            _play.StartHiding(_random.Next(3), now);
        }
        PetHitArea.IsHitTestVisible = false;
        RenderPlayTransition();
        _playFrameTimer.Start();
    }

    private void PlayFrameTimer_OnTick(object? sender, EventArgs e)
    {
        if (RefreshFullScreenSuppression()) return;
        var before = _play.Phase;
        var now = Environment.TickCount64;
        _play.Advance(now);
        if (before != _play.Phase) RenderPlayTransition();
        if (_play.Phase == PetPlayPhase.Walking && PetHitArea.Visibility == Visibility.Visible &&
            _play.Deadline - now <= PetPlaySession.WalkMilliseconds - WalkExitMilliseconds)
            HidePlayPet();
        if (_play.Phase == PetPlayPhase.Hiding) PlayPanel.UpdateHiding(_play, now);
        if (_play.Phase == PetPlayPhase.Found && _play.Deadline - now < PetPlaySession.RevealMilliseconds - 450)
            PlayPanel.CuddleMouse();
    }

    private void PlayPanel_OnCloudChosen(object? sender, int cloud)
    {
        if (RefreshFullScreenSuppression()) return;
        if (_play.Phase != PetPlayPhase.Hiding) return;
        var before = _play.Phase;
        var found = _play.ChooseCloud(cloud, Environment.TickCount64);
        if (before != _play.Phase) RenderPlayTransition();
        else if (!found) PlayPanel.ShowWrongCloud(cloud);
    }

    private void RenderPlayTransition()
    {
        switch (_play.Phase)
        {
            case PetPlayPhase.WalkGreeting:
                PlayPanel.ShowSpeech("我去云边走走，等我一下～", PetPlaySession.GreetingMilliseconds);
                PlayPopup.IsOpen = true;
                break;
            case PetPlayPhase.Walking:
                PlayPopup.IsOpen = false;
                PetArtwork.SetWalking(true);
                PlayTravel.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(0, -34, TimeSpan.FromMilliseconds(WalkExitMilliseconds))
                    {
                        AccelerationRatio = 0.18
                    });
                FadePlayPetOut(delay: 340, duration: WalkExitMilliseconds - 340);
                break;
            case PetPlayPhase.Returning:
                RestorePetAfterPlay(acceptInput: false);
                var arrival = TimeSpan.FromMilliseconds(PetPlaySession.ReturnMilliseconds - 40);
                FadePlayPetIn(arrival);
                PlayTravel.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18, 0, arrival)
                {
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                break;
            case PetPlayPhase.ShowingTreasure:
                PetArtwork.SetTreasure(_play.Treasure);
                PetHitArea.IsHitTestVisible = true;
                PlayPanel.ShowSpeech(_play.Treasure!.Encounter, PetPlaySession.TreasureMilliseconds);
                PlayPopup.IsOpen = true;
                SavePlayOutcome("walk:returned", PetPlayKind.Walk);
                break;
            case PetPlayPhase.HideGreeting:
                PlayPanel.ShowSpeech("我去躲起来啦，来找我呀～", PetPlaySession.GreetingMilliseconds);
                PlayPopup.IsOpen = true;
                FadePlayPetOut(delay: PetPlaySession.GreetingMilliseconds - 340, duration: 340);
                break;
            case PetPlayPhase.Hiding:
                HidePlayPet();
                PlayPanel.ShowClouds();
                break;
            case PetPlayPhase.Found:
            case PetPlayPhase.TimedOut:
                var found = _play.Phase == PetPlayPhase.Found;
                PlayPanel.ShowReveal(_play.HiddenCloud, found);
                SavePlayOutcome(found ? "hide:found" : "hide:timeout", PetPlayKind.HideAndSeek);
                break;
            case PetPlayPhase.Idle:
                var wasHidden = PetHitArea.Visibility == Visibility.Hidden;
                CancelPlay();
                if (wasHidden) FadePlayPetIn(TimeSpan.FromMilliseconds(460));
                break;
        }
    }

    private async void SavePlayOutcome(string outcome, PetPlayKind kind)
    {
        _lastPlayKind = kind;
        try
        {
            // No treasure, active phase or deadline is written to SQLite.
            await _database.SetStateAsync("play.last", outcome);
        }
        catch (Exception ex)
        {
            await _logService.ErrorAsync("play_history_save_failed", ex);
        }
    }

    private void FadePlayPetOut(long delay, int duration)
    {
        PetHitArea.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(duration))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void FadePlayPetIn(TimeSpan duration)
    {
        PetHitArea.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration)
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void HidePlayPet()
    {
        _stateController.SetSuspended(true);
        PetHitArea.Visibility = Visibility.Hidden;
    }

    private void RestorePetAfterPlay(bool acceptInput = true)
    {
        var wasHidden = PetHitArea.Visibility == Visibility.Hidden;
        PetArtwork.SetWalking(false);
        PetHitArea.BeginAnimation(OpacityProperty, null);
        PlayTravel.BeginAnimation(TranslateTransform.XProperty, null);
        PetHitArea.Opacity = 1;
        PetHitArea.Visibility = Visibility.Visible;
        PetHitArea.IsHitTestVisible = acceptInput;
        if (wasHidden && IsVisible && !_isFullScreenSuppressed)
        {
            _stateController.SetSuspended(false);
            UpdateDayPhase();
        }
    }

    private void CancelPlay(bool resumeIdle = true)
    {
        _playFrameTimer.Stop();
        _playTimer.Stop();
        _play.Reset();
        PlayPopup.IsOpen = false;
        PlayPanel.Reset();
        PetArtwork.SetTreasure(null);
        RestorePetAfterPlay();
        _playIdleSince = Environment.TickCount64;
        if (resumeIdle && !_isClosing && IsVisible && !_isFullScreenSuppressed)
        {
            RestartIdleTimers();
            ScheduleNextCompanionMessage();
            ScheduleNextPlay();
        }
    }

    private void PlayPopup_OnOpened(object? sender, EventArgs e)
    {
        ScreenPlacementHelper.PlacePopup(this, PlayPopup, PlayPanel.Width, PlayPanel.Height);
        PlayPanel.SetSpeechSide(PlayPopup.HorizontalOffset >= 0);
        PopupWindowHelper.RefreshPosition(this, PlayPopup);
    }

    private void DisposePlay()
    {
        _isClosing = true;
        CancelPlay(resumeIdle: false);
        _playTimer.Tick -= PlayTimer_OnTick;
        _playFrameTimer.Tick -= PlayFrameTimer_OnTick;
        PlayPanel.CloudChosen -= PlayPanel_OnCloudChosen;
    }
}

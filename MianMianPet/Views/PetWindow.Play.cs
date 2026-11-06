using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MianMianPet.Animation;
using MianMianPet.Configuration;
using MianMianPet.Infrastructure;
using MianMianPet.Models;

namespace MianMianPet.Views;

public partial class PetWindow
{
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
        if (RefreshFullScreenSuppression()) return;
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
            PlayPanel.ShowWalking();
        }
        else
        {
            _play.StartHiding(_random.Next(3), now);
            PlayPanel.ShowHiding();
        }
        PetHitArea.IsHitTestVisible = false;
        PetHitArea.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)));
        PlayPopup.IsOpen = true;
        _playFrameTimer.Start();
    }

    private void PlayFrameTimer_OnTick(object? sender, EventArgs e)
    {
        if (RefreshFullScreenSuppression()) return;
        var before = _play.Phase;
        var now = Environment.TickCount64;
        _play.Advance(now);
        if (before != _play.Phase) RenderPlayTransition();
        if (_play.Phase == PetPlayPhase.Hiding) PlayPanel.UpdateHiding(_play, now);
        if (_play.Phase == PetPlayPhase.Found && _play.Deadline - now < PetPlaySession.RevealMilliseconds - 450)
            PlayPanel.CuddleMouse(_play.HiddenCloud);
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
            case PetPlayPhase.ShowingTreasure:
                PetArtwork.SetTreasure(_play.Treasure);
                RestorePetAfterPlay();
                _stateController.PlayWakeReaction();
                PlayPanel.ShowTreasure(_play.Treasure!);
                SavePlayOutcome("walk:returned", PetPlayKind.Walk);
                break;
            case PetPlayPhase.Found:
            case PetPlayPhase.TimedOut:
                var found = _play.Phase == PetPlayPhase.Found;
                PlayPanel.ShowReveal(_play.HiddenCloud, found);
                SavePlayOutcome(found ? "hide:found" : "hide:timeout", PetPlayKind.HideAndSeek);
                break;
            case PetPlayPhase.Idle:
                CancelPlay();
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

    private void RestorePetAfterPlay()
    {
        PetHitArea.BeginAnimation(OpacityProperty, null);
        PetHitArea.Opacity = 1;
        PetHitArea.IsHitTestVisible = true;
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

    private void PlayPopup_OnOpened(object? sender, EventArgs e) =>
        ScreenPlacementHelper.PlacePopup(this, PlayPopup, PlayPanel.Width, PlayPanel.Height);

    private void DisposePlay()
    {
        _isClosing = true;
        CancelPlay(resumeIdle: false);
        _playTimer.Tick -= PlayTimer_OnTick;
        _playFrameTimer.Tick -= PlayFrameTimer_OnTick;
        PlayPanel.CloudChosen -= PlayPanel_OnCloudChosen;
    }
}

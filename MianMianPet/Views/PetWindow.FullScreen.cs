using System.Windows;
using System.Windows.Threading;
using MianMianPet.Infrastructure;

namespace MianMianPet.Views;

public partial class PetWindow
{
    private readonly DispatcherTimer _fullScreenTimer;
    private bool _isFullScreenSuppressed;
    internal Func<bool> FullScreenDetector { get; set; } = UserActivityHelper.IsOtherApplicationFullScreen;

    private void FullScreenTimer_OnTick(object? sender, EventArgs e) => RefreshFullScreenSuppression();

    private bool RefreshFullScreenSuppression()
    {
        var suppressed = FullScreenDetector();
        if (_isFullScreenSuppressed == suppressed) return suppressed;
        _isFullScreenSuppressed = suppressed;
        if (suppressed)
        {
            // Close native popups as well as hiding the pet; opacity alone leaves hit targets alive.
            PetRoot.Visibility = Visibility.Hidden;
            _stateController.SetSuspended(true);
            CancelPlay(resumeIdle: false);
            _ambientTimer.Stop();
            _sleepTimer.Stop();
            _companionTimer.Stop();
            _dayPhaseTimer.Stop();
            PetHitArea.ContextMenu.IsOpen = false;
            _isPointerDown = false;
            _didDrag = false;
            _lastPetPoint = null;
            PetHitArea.ReleaseMouseCapture();
            _viewModel.SetPresentationSuppressed(true);
            _inputWindow?.Hide();
        }
        else
        {
            _viewModel.SetPresentationSuppressed(false);
            PetRoot.Visibility = Visibility.Visible;
            _stateController.SetSuspended(!IsVisible);
            _playIdleSince = Environment.TickCount64;
            if (IsVisible)
            {
                UpdateDayPhase();
                _dayPhaseTimer.Start();
                RestartIdleTimers();
                ScheduleNextCompanionMessage();
                ScheduleNextPlay();
            }
        }
        return suppressed;
    }
}

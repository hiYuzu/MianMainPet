using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MianMianPet.Infrastructure;

namespace MianMianPet.Views;

public partial class PetWindow
{
    private readonly DispatcherTimer _fullScreenTimer;
    private bool _isFullScreenSuppressed;

    private void FullScreenTimer_OnTick(object? sender, EventArgs e) => RefreshFullScreenSuppression();

    private bool RefreshFullScreenSuppression()
    {
        var suppressed = UserActivityHelper.IsOtherApplicationFullScreen();
        if (_isFullScreenSuppressed == suppressed) return suppressed;
        _isFullScreenSuppressed = suppressed;
        if (suppressed)
        {
            // Close native popups as well as hiding the pet; opacity alone leaves hit targets alive.
            PetRoot.Visibility = Visibility.Hidden;
            PlayPopup.PopupAnimation = PopupAnimation.None;
            BubblePopup.PopupAnimation = PopupAnimation.None;
            _stateController.SetSuspended(true);
            CancelPlay(resumeIdle: false);
            _ambientTimer.Stop();
            _sleepTimer.Stop();
            _companionTimer.Stop();
            _dayPhaseTimer.Stop();
            PetHitArea.ContextMenu.IsOpen = false;
            PetHitArea.ReleaseMouseCapture();
            _isPointerDown = false;
            _lastPetPoint = null;
            _viewModel.SetPresentationSuppressed(true);
            _inputWindow?.Hide();
        }
        else
        {
            _viewModel.SetPresentationSuppressed(false);
            PlayPopup.PopupAnimation = PopupAnimation.Fade;
            BubblePopup.PopupAnimation = PopupAnimation.Fade;
            PetRoot.Visibility = Visibility.Visible;
            _stateController.SetSuspended(false);
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

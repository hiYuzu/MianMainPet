using System.Windows.Threading;
using MianMianPet.Views;

namespace MianMianPet.Animation;

public sealed class PetStateController : IDisposable
{
    private readonly PetVisual _visual;
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _pettingTimer;
    private readonly Random _random = new();
    private bool _suspended;
    private bool _disposed;

    public PetStateController(PetVisual visual)
    {
        _visual = visual;
        _pettingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _pettingTimer.Tick += (_, _) => SetState(PetState.Idle);
        _blinkTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = NextBlinkInterval()
        };
        _blinkTimer.Tick += BlinkTimerOnTick;
        _blinkTimer.Start();
    }

    public PetState CurrentState { get; private set; } = PetState.Idle;

    public void SetState(PetState state)
    {
        if (CurrentState == state && state is not PetState.Error)
        {
            return;
        }

        CurrentState = state;
        _pettingTimer.Stop();
        if (_suspended) return;
        _visual.ApplyState(state);
        if (state is PetState.Petted) _pettingTimer.Start();
    }

    public void PlayClickReaction() { if (!_suspended) _visual.PlayClick(); }

    public void PlayWakeReaction() { if (!_suspended) _visual.PlayWake(); }

    internal void SetSuspended(bool suspended)
    {
        if (_suspended == suspended || _disposed) return;
        _suspended = suspended;
        _blinkTimer.Stop();
        _pettingTimer.Stop();
        _visual.SetAnimationsSuspended(suspended);
        if (!suspended && !_disposed)
        {
            _visual.ApplyState(CurrentState);
            if (CurrentState is PetState.Petted) _pettingTimer.Start();
            _blinkTimer.Interval = NextBlinkInterval();
            _blinkTimer.Start();
        }
    }

    private async void BlinkTimerOnTick(object? sender, EventArgs e)
    {
        _blinkTimer.Stop();
        if (_suspended || _disposed) return;
        if (CurrentState is PetState.Idle or PetState.Hovered or PetState.Listening)
        {
            _visual.SetBlink(true);
            await Task.Delay(_random.Next(100, 155));
            if (_suspended || _disposed) return;
            if (CurrentState is PetState.Idle or PetState.Hovered or PetState.Listening)
            {
                _visual.SetBlink(false);
            }
        }

        _blinkTimer.Interval = NextBlinkInterval();
        _blinkTimer.Start();
    }

    private TimeSpan NextBlinkInterval() => TimeSpan.FromSeconds(_random.NextDouble() * 5 + 4);

    public void Dispose()
    {
        _disposed = true;
        _blinkTimer.Stop();
        _pettingTimer.Stop();
    }
}

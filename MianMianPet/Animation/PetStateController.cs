using System.Windows.Threading;
using MianMianPet.Views;

namespace MianMianPet.Animation;

public sealed class PetStateController : IDisposable
{
    private readonly PetVisual _visual;
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _pettingTimer;
    private readonly Random _random = new();

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
        _visual.ApplyState(state);
        if (state is PetState.Petted) _pettingTimer.Start();
    }

    public void PlayClickReaction() => _visual.PlayClick();

    public void PlayWakeReaction() => _visual.PlayWake();

    private async void BlinkTimerOnTick(object? sender, EventArgs e)
    {
        _blinkTimer.Stop();
        if (CurrentState is PetState.Idle or PetState.Hovered or PetState.Listening)
        {
            _visual.SetBlink(true);
            await Task.Delay(_random.Next(100, 155));
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
        _blinkTimer.Stop();
        _pettingTimer.Stop();
    }
}

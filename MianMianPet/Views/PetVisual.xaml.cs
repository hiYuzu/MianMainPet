using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MianMianPet.Animation;
using MianMianPet.Configuration;
using MianMianPet.Models;

namespace MianMianPet.Views;

public partial class PetVisual : UserControl
{
    private Storyboard? _activeStateStoryboard;
    private Storyboard? _ambientLoopStoryboard;
    private bool _isAmbientSceneVisible;
    private bool _animationsSuspended;
    private PetState _state;
    private PetDayPhase? _dayPhase;
    private Storyboard? _breathingStoryboard;
    private Storyboard? _twinkleStoryboard;
    private Storyboard? _reactionStoryboard;

    public PetVisual()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => RefreshAnimations();
    }

    internal void SetAnimationsSuspended(bool suspended)
    {
        _animationsSuspended = suspended;
        RefreshAnimations();
    }

    private void RefreshAnimations()
    {
        if (IsVisible && !_animationsSuspended)
        {
            if (_breathingStoryboard is not null) return;
            _breathingStoryboard = (Storyboard)Resources["BreathingStoryboard"];
            _breathingStoryboard.Begin(this, isControllable: true);
            ApplyState(_state);
            return;
        }

        _breathingStoryboard?.Remove(this);
        _twinkleStoryboard?.Remove(this);
        _activeStateStoryboard?.Remove(this);
        _ambientLoopStoryboard?.Remove(this);
        _reactionStoryboard?.Remove(this);
        SetWalking(false);
        _breathingStoryboard = _twinkleStoryboard = _activeStateStoryboard = _ambientLoopStoryboard = _reactionStoryboard = null;
        _dayPhase = null;
        _isAmbientSceneVisible = false;
        PoseRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        GrassScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        foreach (var target in new UIElement[] { AmbientScene, AmbientSparkleOverlay, TimeOfDayAtmosphere, SunDecoration, MoonDecoration })
            target.BeginAnimation(OpacityProperty, null);
        AmbientScene.Opacity = AmbientSparkleOverlay.Opacity = 0;
        SetBlink(false);
    }

    public void ApplyState(PetState state)
    {
        _state = state;
        if (_animationsSuspended || !IsVisible) return;
        var previousAngle = PoseRotate.Angle + StateRotate.Angle;
        _activeStateStoryboard?.Remove(this);
        _activeStateStoryboard = null;
        var pose = state switch
        {
            PetState.Hovered => -3,
            PetState.Listening => -4,
            _ => 0
        };
        PoseRotate.Angle = pose;
        PoseRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(previousAngle, pose, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = (EasingFunctionBase)Resources["SettleEase"],
                FillBehavior = FillBehavior.Stop
            });
        TalkMouth.Visibility = Visibility.Collapsed;
        WorriedMouth.Visibility = Visibility.Collapsed;
        IdleMouth.Visibility = Visibility.Visible;
        SleepZOverlay.Visibility = Visibility.Collapsed;
        PettingHeart.Visibility = Visibility.Collapsed;
        if (state is not PetState.Sleeping)
        {
            SetBlink(false);
        }

        switch (state)
        {
            case PetState.Petted:
                SetBlink(true);
                PettingHeart.Visibility = Visibility.Visible;
                StartStateStoryboard("PettingStoryboard");
                break;
            case PetState.Thinking:
                StartStateStoryboard("ThinkingStoryboard");
                break;
            case PetState.Speaking:
                IdleMouth.Visibility = Visibility.Collapsed;
                TalkMouth.Visibility = Visibility.Visible;
                StartStateStoryboard("SpeakingStoryboard");
                break;
            case PetState.Error:
                IdleMouth.Visibility = Visibility.Collapsed;
                WorriedMouth.Visibility = Visibility.Visible;
                StartStateStoryboard("ErrorStoryboard");
                break;
            case PetState.Sleeping:
                SetBlink(true);
                SleepZOverlay.Visibility = Visibility.Visible;
                StartStateStoryboard("SleepingStoryboard");
                break;
        }
    }

    public void PlayClick() => PlayReaction("ClickStoryboard");

    public void PlayWake() => PlayReaction("WakeStoryboard");

    internal void SetWalking(bool walking)
    {
        if (walking == (WalkingLegs.Visibility == Visibility.Visible)) return;
        var stride = (Storyboard)Resources["WalkingStoryboard"];
        if (walking)
        {
            PetImage.Clip = (Geometry)Resources["WalkingBodyClip"];
            WalkingLegs.Visibility = Visibility.Visible;
            stride.Begin(this, isControllable: true);
        }
        else
        {
            stride.Remove(this);
            WalkingLegs.Visibility = Visibility.Collapsed;
            PetImage.Clip = null;
        }
    }

    internal void SetTreasure(WalkTreasure? treasure)
    {
        TreasureAdornment.Visibility = treasure is null ? Visibility.Collapsed : Visibility.Visible;
        TreasureAdornment.Data = treasure is null ? null : Geometry.Parse(treasure.Shape);
        if (treasure is not null)
            TreasureAdornment.Fill = (Brush)new BrushConverter().ConvertFromString(treasure.Color)!;
    }

    public void SetBlink(bool closed)
    {
        OpenEyes.Visibility = closed ? Visibility.Collapsed : Visibility.Visible;
        ClosedEyes.Visibility = closed ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetAmbientSceneVisible(bool visible)
    {
        if (_animationsSuspended || !IsVisible) return;
        if (_isAmbientSceneVisible == visible)
        {
            return;
        }

        _isAmbientSceneVisible = visible;
        if (visible)
        {
            _ambientLoopStoryboard?.Remove(this);
            _ambientLoopStoryboard = ((Storyboard)Resources["AmbientLoopStoryboard"]).Clone();
            _ambientLoopStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        }

        var fade = new DoubleAnimation
        {
            To = visible ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(visible ? 1350 : 320),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        fade.Completed += (_, _) =>
        {
            if (!_isAmbientSceneVisible)
            {
                _ambientLoopStoryboard?.Remove(this);
                _ambientLoopStoryboard = null;
            }
        };
        AmbientScene.BeginAnimation(OpacityProperty, fade);

        var sparkleFade = new DoubleAnimation
        {
            To = visible ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(visible ? 720 : 180),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        AmbientSparkleOverlay.BeginAnimation(OpacityProperty, sparkleFade);

        var grass = new DoubleAnimation
        {
            To = visible ? 1 : 0.72,
            Duration = TimeSpan.FromMilliseconds(visible ? 1500 : 300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        GrassScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grass);
    }

    internal void ApplyDayPhase(PetDayPhase phase)
    {
        if (_animationsSuspended || !IsVisible || _dayPhase == phase) return;
        _dayPhase = phase;
        if (phase is PetDayPhase.Night)
        {
            _twinkleStoryboard = (Storyboard)Resources["TimeOfDayTwinkleStoryboard"];
            _twinkleStoryboard.Begin(this, isControllable: true);
        }
        else
        {
            _twinkleStoryboard?.Remove(this);
            _twinkleStoryboard = null;
        }
        var (center, edge, atmosphereOpacity, sunOpacity, moonOpacity) = phase switch
        {
            PetDayPhase.Dawn =>
                (Color.FromArgb(0x62, 0xFF, 0xD2, 0x9B), Color.FromArgb(0x00, 0xFF, 0xF3, 0xE5), 0.38, 0.68, 0.0),
            PetDayPhase.Day =>
                (Color.FromArgb(0x45, 0xFF, 0xEE, 0xBD), Color.FromArgb(0x00, 0xFF, 0xFA, 0xEE), 0.25, 0.34, 0.0),
            PetDayPhase.Dusk =>
                (Color.FromArgb(0x68, 0xFF, 0xAD, 0x8F), Color.FromArgb(0x00, 0xA7, 0x85, 0xC8), 0.43, 0.48, 0.0),
            _ =>
                (Color.FromArgb(0x62, 0x68, 0x64, 0xA8), Color.FromArgb(0x00, 0x35, 0x32, 0x61), 0.46, 0.0, 0.92)
        };

        DayPhaseGlow.Fill = new RadialGradientBrush(center, edge);
        AnimateOpacity(TimeOfDayAtmosphere, atmosphereOpacity, 900);
        AnimateOpacity(SunDecoration, sunOpacity, 700);
        AnimateOpacity(MoonDecoration, moonOpacity, 700);
    }

    private void StartStateStoryboard(string key)
    {
        _activeStateStoryboard = (Storyboard)Resources[key];
        _activeStateStoryboard.Begin(this, isControllable: true);
    }

    private void PlayReaction(string key)
    {
        if (_animationsSuspended || !IsVisible) return;
        _reactionStoryboard?.Remove(this);
        _reactionStoryboard = (Storyboard)Resources[key];
        _reactionStoryboard.Begin(this, isControllable: true);
    }

    private static void AnimateOpacity(UIElement target, double opacity, int durationMilliseconds)
    {
        target.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = opacity,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }
}

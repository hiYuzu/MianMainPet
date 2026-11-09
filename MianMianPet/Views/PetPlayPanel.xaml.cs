using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MianMianPet.Animation;

namespace MianMianPet.Views;

public partial class PetPlayPanel : UserControl
{
    private readonly Button[] _clouds;
    private TranslateTransform RevealTranslate => (TranslateTransform)RevealPet.RenderTransform;
    private Point? _lastCuddleTarget;
    private int _peekingCloud = -1;

    public PetPlayPanel()
    {
        InitializeComponent();
        _clouds = [CloudOne, CloudTwo, CloudThree];
    }

    internal event EventHandler<int>? CloudChosen;

    internal void ShowSpeech(string message, long visibleMilliseconds)
    {
        const int fadeMilliseconds = 230;
        CloudScene.Visibility = Visibility.Collapsed;
        SpeechText.Text = message;
        Speech.Visibility = Visibility.Visible;
        SpeechContent.BeginAnimation(OpacityProperty, null);
        SpeechContent.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(fadeMilliseconds))
        {
            BeginTime = TimeSpan.FromMilliseconds(visibleMilliseconds - fadeMilliseconds)
        });
    }

    internal void SetSpeechSide(bool isRightOfPet)
    {
        SpeechPuffs.HorizontalAlignment = isRightOfPet ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        PuffDirection.ScaleX = isRightOfPet ? 1 : -1;
    }

    internal void ShowClouds()
    {
        Speech.Visibility = Visibility.Collapsed;
        SpeechContent.BeginAnimation(OpacityProperty, null);
        CloudScene.Visibility = Visibility.Visible;
    }

    internal void UpdateHiding(PetPlaySession session, long now) =>
        SetPeekingCloud(session.IsPeeking(now) ? session.HiddenCloud : -1);

    private void SetPeekingCloud(int cloud)
    {
        if (_peekingCloud == cloud) return;
        _peekingCloud = cloud;
        for (var i = 0; i < _clouds.Length; i++)
        {
            var button = _clouds[i];
            if (button.Template?.FindName("PeekEars", button) is UIElement ears)
                ears.Visibility = i == cloud ? Visibility.Visible : Visibility.Hidden;
        }
    }

    internal void ShowWrongCloud(int index)
    {
        var button = _clouds[index];
        if (button.Template?.FindName("CloudBody", button) is not FrameworkElement body ||
            body.RenderTransform is not TranslateTransform movement) return;

        if (movement.IsFrozen)
        {
            movement = movement.Clone();
            body.RenderTransform = movement;
        }

        var shake = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(260),
            FillBehavior = FillBehavior.Stop
        };
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(-4, KeyTime.FromPercent(0.22)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromPercent(0.55)));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        movement.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    internal void ShowReveal(int cloud, bool found)
    {
        foreach (var button in _clouds) button.IsEnabled = false;
        SetPeekingCloud(-1);
        var chosen = _clouds[cloud];
        Canvas.SetLeft(RevealPet, Canvas.GetLeft(chosen) + (chosen.Width - RevealPet.Width) / 2);
        Canvas.SetTop(RevealPet, Canvas.GetTop(chosen) - 49);
        Panel.SetZIndex(RevealPet, found ? 1 : -1);
        RevealPet.Visibility = Visibility.Visible;
        RevealPet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            FillBehavior = FillBehavior.Stop
        });
        RevealTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(440))
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = found ? 0.4 : 0 }
        });
        GameStage.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280))
        {
            BeginTime = TimeSpan.FromMilliseconds(PetPlaySession.RevealMilliseconds - 280)
        });
    }

    internal void CuddleMouse()
    {
        var mouse = Mouse.GetPosition(GameStage);
        if (!new Rect(GameStage.RenderSize).Contains(mouse)) return;
        var target = new Point(Math.Clamp(mouse.X - 46, 4, Width - RevealPet.Width - 4) - Canvas.GetLeft(RevealPet),
            Math.Clamp(mouse.Y - 74, 0, 28) - Canvas.GetTop(RevealPet));
        if (_lastCuddleTarget is Point previous && (target - previous).LengthSquared < 1) return;
        if (_lastCuddleTarget is null) RevealPet.ApplyState(PetState.Petted);
        _lastCuddleTarget = target;
        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        RevealTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(target.X, duration) { EasingFunction = easing });
        RevealTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(target.Y, duration) { EasingFunction = easing });
    }

    internal void Reset()
    {
        Speech.Visibility = Visibility.Collapsed;
        SpeechContent.BeginAnimation(OpacityProperty, null);
        CloudScene.Visibility = Visibility.Collapsed;
        GameStage.BeginAnimation(OpacityProperty, null);
        RevealPet.Visibility = Visibility.Collapsed;
        RevealPet.ApplyState(PetState.Idle);
        RevealPet.BeginAnimation(OpacityProperty, null);
        RevealTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        RevealTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _lastCuddleTarget = null;
        SetPeekingCloud(-1);
        foreach (var button in _clouds)
        {
            button.IsEnabled = true;
            if (button.Template?.FindName("CloudBody", button) is FrameworkElement body &&
                body.RenderTransform is TranslateTransform { IsFrozen: false } movement)
                movement.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }

    private void Cloud_OnClick(object sender, RoutedEventArgs e) =>
        CloudChosen?.Invoke(this, int.Parse((string)((Button)sender).Tag));
}

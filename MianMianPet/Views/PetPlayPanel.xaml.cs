using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MianMianPet.Animation;
using MianMianPet.Models;

namespace MianMianPet.Views;

public partial class PetPlayPanel : UserControl
{
    private Button[] Clouds => [CloudOne, CloudTwo, CloudThree];
    private TranslateTransform RevealTranslate => (TranslateTransform)RevealPet.RenderTransform;
    private bool _cuddling;

    public PetPlayPanel() => InitializeComponent();

    internal event EventHandler<int>? CloudChosen;

    internal void ShowWalking()
    {
        Reset();
        GameStage.Visibility = Visibility.Collapsed;
        WalkContent.Visibility = Visibility.Visible;
        TitleText.Text = "散步";
        HintText.Text = "去云边走走，很快就回来～";
        CountdownText.Text = "散步中";
        TreasureIcon.Data = Geometry.Parse(WalkTreasures.All[4].Shape);
        TreasureIcon.Fill = new SolidColorBrush(Color.FromRgb(255, 240, 247));
        TreasureName.Text = "绵绵出门啦";
        EncounterText.Text = "沿着软软的云，找一点小惊喜……";
    }

    internal void ShowTreasure(WalkTreasure treasure)
    {
        HintText.Text = "我回来啦！看看今天捡到了什么。";
        CountdownText.Text = "带回来啦";
        TreasureIcon.Data = Geometry.Parse(treasure.Shape);
        TreasureIcon.Fill = (Brush)new BrushConverter().ConvertFromString(treasure.Color)!;
        TreasureName.Text = treasure.Name;
        EncounterText.Text = treasure.Encounter;
    }

    internal void ShowHiding()
    {
        Reset();
        GameStage.Visibility = Visibility.Visible;
        WalkContent.Visibility = Visibility.Collapsed;
        TitleText.Text = "捉迷藏";
        HintText.Text = "藏进软软的云里，快来找我呀。";
        CountdownText.Text = "10s";
    }

    internal void UpdateHiding(PetPlaySession session, long now)
    {
        CountdownText.Text = $"{session.SecondsLeft(now)}s";
        for (var i = 0; i < Clouds.Length; i++)
        {
            var button = Clouds[i];
            button.ApplyTemplate();
            if (button.Template.FindName("PeekEars", button) is UIElement ears)
                ears.Visibility = i == session.HiddenCloud && session.IsPeeking(now) ? Visibility.Visible : Visibility.Hidden;
        }
    }

    internal void ShowWrongCloud(int index)
    {
        HintText.Text = "这朵只有软软的云，再试试～";
        var button = Clouds[index];
        if (button.Template.FindName("CloudBody", button) is FrameworkElement body)
        {
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(240) };
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(-3, KeyTime.FromPercent(0.25)));
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromPercent(0.65)));
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            var movement = new TranslateTransform();
            body.RenderTransform = movement;
            movement.BeginAnimation(TranslateTransform.XProperty, shake);
        }
    }

    internal void ShowReveal(int cloud, bool found)
    {
        foreach (var button in Clouds)
        {
            button.IsEnabled = false;
            if (button.Template.FindName("PeekEars", button) is UIElement ears) ears.Visibility = Visibility.Hidden;
        }
        CountdownText.Text = string.Empty;
        HintText.Text = found ? "找到啦！蹦出来蹭蹭你 ♡" : "时间到～我在这里呀，下次再一起玩。";
        RevealPet.Visibility = Visibility.Visible;
        Canvas.SetLeft(RevealPet, cloud * 104);
        Canvas.SetTop(RevealPet, 12);
        Panel.SetZIndex(RevealPet, found ? 1 : -1);
        RevealTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = found ? 0.6 : 0 }
        });
        RevealPet.PlayWake();
    }

    internal void CuddleMouse(int cloud)
    {
        if (!_cuddling)
        {
            _cuddling = true;
            RevealPet.ApplyState(PetState.Petted);
        }
        var mouse = Mouse.GetPosition(GameStage);
        var x = Math.Clamp(mouse.X - 24, 0, 210) - cloud * 104;
        var y = Math.Clamp(mouse.Y - 42, 0, 36) - 12;
        RevealTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(180)));
        RevealTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(180)));
    }

    internal void Reset()
    {
        CountdownText.Text = string.Empty;
        RevealPet.Visibility = Visibility.Collapsed;
        RevealPet.ApplyState(PetState.Idle);
        RevealTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        RevealTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _cuddling = false;
        foreach (var button in Clouds)
        {
            button.IsEnabled = true;
            button.ApplyTemplate();
            if (button.Template.FindName("PeekEars", button) is UIElement ears) ears.Visibility = Visibility.Hidden;
            if (button.Template.FindName("CloudBody", button) is FrameworkElement body && body.RenderTransform is TranslateTransform movement)
                movement.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }

    private void Cloud_OnClick(object sender, RoutedEventArgs e) =>
        CloudChosen?.Invoke(this, int.Parse((string)((Button)sender).Tag));
}

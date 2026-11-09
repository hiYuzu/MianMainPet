using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MianMianPet.Animation;
using MianMianPet.Infrastructure;
using MianMianPet.Services;
using MianMianPet.ViewModels;
using MianMianPet.Views;

namespace MianMianPet.Tests;

internal static class WindowRegressionTests
{
    internal static async Task RunAsync(PetWindow window, PetViewModel viewModel, Action<bool, string> check,
        Window? foreground = null)
    {
        var state = Field<PetStateController>(window, "_stateController");
        var settings = Field<SettingsService>(window, "_settingsService");
        var bubble = Field<Popup>(window, "BubblePopup");
        var bubbleControl = Field<ChatBubble>(window, "BubbleControl");
        var playPopup = Field<Popup>(window, "PlayPopup");
        var hit = Field<Border>(window, "PetHitArea");
        var topmost = settings.Current.AlwaysOnTop;
        var position = new Point(window.Left, window.Top);
        var ownsForeground = foreground is null;
        foreground ??= CreateForegroundWindow();
        try
        {
            ActivateForegroundWindow(foreground);
            Call(window, "RefreshFullScreenSuppression");
            check(!window.FullScreenDetector(), "window regressions use a controlled non-fullscreen environment");
            var artwork = Field<PetVisual>(window, "PetArtwork");
            var breath = (ScaleTransform)artwork.FindName("BreathScale");
            var reaction = (ScaleTransform)artwork.FindName("ReactionScale");
            var pose = (RotateTransform)artwork.FindName("StateRotate");
            state.SetState(PetState.Thinking);
            state.PlayClickReaction();
            check(breath.HasAnimatedProperties && reaction.HasAnimatedProperties && pose.HasAnimatedProperties,
                "breathing, thinking and click feedback animate independent transforms");
            state.SetState(PetState.Speaking);
            check(((ScaleTransform)artwork.FindName("MouthScale")).HasAnimatedProperties && breath.HasAnimatedProperties,
                "speaking animates the mouth while breathing continues");
            state.SetSuspended(true);
            state.SetSuspended(false);
            check(state.CurrentState == PetState.Speaking, "resuming animation preserves an ongoing speaking state");
            state.SetSuspended(true);
            state.SetState(PetState.Thinking);
            state.SetSuspended(false);
            check(state.CurrentState == PetState.Thinking, "state changes received while suspended are restored");
            state.SetState(PetState.Idle);

            viewModel.HideInput();
            SetProperty(viewModel, nameof(PetViewModel.IsBubbleOpen), false);
            window.Left = 300;
            window.Top = 300;
            check(viewModel.TryShowCompanionMessage("气泡跟随位置测试"), "regression bubble opens");
            await SettleAsync();
            var before = bubbleControl.PointToScreen(new Point());
            var ownerBefore = window.PointToScreen(new Point());
            window.Left += 30;
            window.Top += 15;
            await SettleAsync();
            var after = bubbleControl.PointToScreen(new Point());
            var ownerAfter = window.PointToScreen(new Point());
            check(Math.Abs((after.X - before.X) - (ownerAfter.X - ownerBefore.X)) <= 2 &&
                Math.Abs((after.Y - before.Y) - (ownerAfter.Y - ownerBefore.Y)) <= 2,
                "open bubble follows owner movement without changing its text or size");

            var bubbleWidth = bubbleControl.ActualWidth;
            var dot = (Ellipse)bubbleControl.FindName("DotOne");
            SetProperty(viewModel, nameof(PetViewModel.IsThinking), true);
            await Task.Delay(240);
            check(dot.IsVisible && dot.HasAnimatedProperties && bubbleControl.ActualWidth == bubbleWidth,
                "thinking uses the same card width and starts its pulse");
            Program.Render(bubbleControl, "chat-thinking");
            SetProperty(viewModel, nameof(PetViewModel.BubbleText),
                string.Join("\n\n", Enumerable.Repeat("傍晚的风软软的，绵绵想带你去云边看一看。回来时还可以一起数星星。", 5)));
            SetProperty(viewModel, nameof(PetViewModel.IsThinking), false);
            await SettleAsync();
            var scroller = (ScrollViewer)bubbleControl.FindName("MessageScroller");
            check(!dot.IsVisible && !dot.HasAnimatedProperties && bubbleControl.ActualWidth == bubbleWidth && scroller.ScrollableHeight > 0,
                "long replies scroll in place and stop the thinking pulse");
            check(scroller.ActualHeight >= Math.Min(120, scroller.MaxHeight),
                $"long reply expands the visible reading area ({scroller.ActualHeight} of {scroller.MaxHeight} DIPs)");
            Program.Render(bubbleControl, "chat-reply");
            var scrollbar = (ScrollBar)scroller.Template.FindName("PART_VerticalScrollBar", scroller);
            var track = (Track)scrollbar.Template.FindName("PART_Track", scrollbar);
            ((RoutedCommand)track.IncreaseRepeatButton.Command).Execute(null, track.IncreaseRepeatButton);
            await SettleAsync();
            check(scroller.VerticalOffset > 0, "styled scrollbar still pages through the long reply");

            settings.Current.AlwaysOnTop = false;
            window.ApplySettings();
            await SettleAsync();
            check(!IsTopmost(bubble), "disabling always-on-top removes the bubble native topmost style");
            settings.Current.AlwaysOnTop = true;
            window.ApplySettings();
            await SettleAsync();
            check(IsTopmost(bubble), "enabling always-on-top restores the bubble native topmost style");
            SetProperty(viewModel, nameof(PetViewModel.IsBubbleOpen), false);
            viewModel.ToggleInput();
            var inputWindow = Field<InputWindow>(window, "_inputWindow");
            viewModel.InputText = "今天我们去哪里散步？";
            await Task.Delay(250);
            Program.Render((InputPanel)inputWindow.Content, "chat-input");
            viewModel.HideInput();
            viewModel.InputText = string.Empty;
            settings.Current.AlwaysOnTop = false;
            window.ApplySettings();
            Call(window, "StartPlay", PetPlayKind.Walk);
            await SettleAsync();
            check(playPopup.IsOpen && !IsTopmost(playPopup), "new gameplay popup inherits disabled always-on-top");
            settings.Current.AlwaysOnTop = true;
            window.ApplySettings();
            await SettleAsync();
            check(IsTopmost(playPopup), "open gameplay popup responds to always-on-top changes");
            window.RegisterExternalInteraction();

            SetField(window, "_isPointerDown", true);
            check(hit.CaptureMouse(), "visible pet captures mouse before capture-loss regression");
            Mouse.Capture(null);
            await SettleAsync();
            check(!Field<bool>(window, "_isPointerDown"), "losing real mouse capture clears the pending pointer interaction");

            check(viewModel.TryShowCompanionMessage("隐藏时应一起收起"),
                $"bubble opens before ordinary window hide (busy={viewModel.IsBusy}, input={viewModel.IsInputOpen}, bubble={viewModel.IsBubbleOpen}, full={Field<bool>(viewModel, "_fullScreenSuppressed")}, hidden={Field<bool>(viewModel, "_windowHidden")})");
            await SettleAsync();
            SetProperty(viewModel, nameof(PetViewModel.IsBusy), true);
            state.SetState(PetState.Speaking);
            window.Hide();
            await SettleAsync();
            check(!bubble.IsOpen && !viewModel.IsBubbleOpen && !bubbleControl.IsVisible,
                "ordinary hide closes the native bubble and its view model state");
            check(!Field<DispatcherTimer>(state, "_blinkTimer").IsEnabled &&
                !Field<DispatcherTimer>(window, "_fullScreenTimer").IsEnabled,
                "ordinary hide stops blink and foreground polling timers");
            viewModel.SetPresentationSuppressed(true);
            viewModel.SetPresentationSuppressed(false);
            SetProperty(viewModel, nameof(PetViewModel.IsBubbleOpen), true);
            check(!viewModel.IsBubbleOpen && !viewModel.TryShowCompanionMessage("迟到的消息"),
                "leaving full-screen suppression cannot reopen content while the owner remains hidden");
            window.Show();
            await SettleAsync();
            check(state.CurrentState == PetState.Speaking && Field<DispatcherTimer>(state, "_blinkTimer").IsEnabled,
                "showing the owner resumes the ongoing chat state and blink timer");
            check(!viewModel.IsBubbleOpen, "showing the owner does not replay a hidden bubble");
        }
        finally
        {
            Mouse.Capture(null);
            SetProperty(viewModel, nameof(PetViewModel.IsBusy), false);
            SetProperty(viewModel, nameof(PetViewModel.IsBubbleOpen), false);
            viewModel.SetPresentationSuppressed(false);
            if (!window.IsVisible) window.Show();
            settings.Current.AlwaysOnTop = topmost;
            window.ApplySettings();
            window.Left = position.X;
            window.Top = position.Y;
            state.SetState(PetState.Idle);
            window.RegisterExternalInteraction();
            if (ownsForeground) foreground.Close();
            else ActivateForegroundWindow(foreground);
        }
    }

    private static async Task SettleAsync()
    {
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(80);
    }

    private static bool IsTopmost(Popup popup)
    {
        var source = (HwndSource)PresentationSource.FromVisual(popup.Child)!;
        return (GetWindowLong(source.Handle, -20) & 0x00000008) != 0;
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void SetProperty(object target, string name, object value) =>
        target.GetType().GetProperty(name)!.SetValue(target, value);
    private static void Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    internal static Window CreateForegroundWindow()
    {
        var foreground = new Window
        {
            Title = "绵绵窗口与玩法回归检查", Width = 360, Height = 100, Left = 20, Top = 20,
            ShowInTaskbar = false,
            Content = new TextBlock { Text = "正在检查浮层、隐藏与玩法…", Margin = new Thickness(20) }
        };
        foreground.Show();
        ActivateForegroundWindow(foreground);
        return foreground;
    }

    internal static void ActivateForegroundWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThread = GetCurrentThreadId();
        var attached = AttachThreadInput(currentThread, foregroundThread, true);
        try { window.Activate(); SetForegroundWindow(handle); }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint thread, uint otherThread, bool attach);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}

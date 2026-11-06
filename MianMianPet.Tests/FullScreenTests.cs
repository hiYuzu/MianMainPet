using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MianMianPet.Animation;
using MianMianPet.Infrastructure;
using MianMianPet.Services;
using MianMianPet.ViewModels;
using MianMianPet.Views;
using Rectangle = System.Drawing.Rectangle;

namespace MianMianPet.Tests;

internal static partial class Program
{
    private static async Task TestFullScreenAsync(PetWindow window, PetViewModel viewModel,
        DatabaseService database, SettingsService settings, LogService log)
    {
        Check(UserActivityHelper.CoversMonitor(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1080)), "borderless client covers monitor");
        Check(!UserActivityHelper.CoversMonitor(new Rectangle(0, 0, 1920, 1040), new Rectangle(0, 0, 1920, 1080)), "maximized working area is not full screen");
        Check(!UserActivityHelper.CoversMonitor(new Rectangle(0, 30, 1920, 1050), new Rectangle(0, 0, 1920, 1080)), "title bar is not part of full-screen client");
        Check(UserActivityHelper.CoversMonitor(new Rectangle(-2560, -200, 2560, 1440), new Rectangle(-2560, -200, 2560, 1440)), "secondary monitor with negative coordinates");
        Check(!UserActivityHelper.CoversMonitor(Rectangle.Empty, new Rectangle(0, 0, 1920, 1080)), "empty client cannot suppress desktop pet");

        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--fullscreen-host");
        using var host = Process.Start(start)!;
        try
        {
            Check(await host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "ready", "external test window started");
            async Task Mode(string mode)
            {
                await host.StandardInput.WriteLineAsync(mode);
                await host.StandardInput.FlushAsync();
                Check(await host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) == mode, $"external window switched to {mode}");
            }

            await Mode("maximized");
            Check(!UserActivityHelper.IsOtherApplicationFullScreen(), "real maximized application remains allowed");
            await Mode("normal");
            var session = Field<PetPlaySession>(window, "_play");
            var artwork = Field<PetVisual>(window, "PetArtwork");
            var state = Field<PetStateController>(window, "_stateController");
            foreach (var kind in new[] { PetPlayKind.Walk, PetPlayKind.HideAndSeek })
            {
                Start(window, kind);
                Check(session.IsActive, $"{kind} begins before entering full screen");
                await Mode("fullscreen");
                Check(UserActivityHelper.IsOtherApplicationFullScreen(), "real external borderless full screen is detected");
                await WaitUntil(() => Field<bool>(window, "_isFullScreenSuppressed"));
                Check(!session.IsActive && !Field<Popup>(window, "PlayPopup").IsOpen, $"entering full screen cancels {kind}");
                Check(!Field<Grid>(window, "PetRoot").IsVisible && !viewModel.IsBubbleOpen && !viewModel.IsInputOpen, "full screen hides pet and floating content");
                Check(!Field<DispatcherTimer>(window, "_playFrameTimer").IsEnabled &&
                    !Field<DispatcherTimer>(window, "_ambientTimer").IsEnabled &&
                    !Field<DispatcherTimer>(window, "_companionTimer").IsEnabled &&
                    !Field<DispatcherTimer>(state, "_blinkTimer").IsEnabled, "full screen stops activity and blink timers");
                Check(Field<Storyboard?>(artwork, "_breathingStoryboard") is null &&
                    Field<Storyboard?>(artwork, "_ambientLoopStoryboard") is null, "full screen removes ongoing animation clocks");
                SetField(window, "_playIdleSince", Environment.TickCount64 - 80000);
                Check(!TryIdle(window, TimeSpan.Zero), "idle scheduler cannot start a game in full screen");
                Start(window, kind);
                Check(!session.IsActive, "direct start is also guarded");
                Call(window, "AmbientTimer_OnTick", null, EventArgs.Empty);
                Call(window, "SleepTimer_OnTick", null, EventArgs.Empty);
                Call(window, "CompanionTimer_OnTick", null, EventArgs.Empty);
                Check(!Field<bool>(artwork, "_isAmbientSceneVisible") && state.CurrentState != PetState.Sleeping,
                    "queued ambient, sleep and companion callbacks stay silent");
                Check(!viewModel.TryShowCompanionMessage("test"), "view model rejects automatic messages while suppressed");
                typeof(PetViewModel).GetProperty(nameof(PetViewModel.IsBubbleOpen))!.SetValue(viewModel, true);
                Check(!viewModel.IsBubbleOpen, "late asynchronous response cannot reopen bubble");
                await Mode("normal");
                await WaitUntil(() => !Field<bool>(window, "_isFullScreenSuppressed"));
                Check(Field<Grid>(window, "PetRoot").IsVisible && !session.IsActive && !viewModel.IsBubbleOpen,
                    "leaving full screen restores pet without replaying game or bubble");
                Check(!TryIdle(window, TimeSpan.Zero), "leaving full screen restarts the idle grace period");
            }

            viewModel.ToggleInput();
            Check(viewModel.IsInputOpen, "input opens before full screen");
            await Mode("fullscreen");
            await WaitUntil(() => Field<bool>(window, "_isFullScreenSuppressed"));
            Check(!viewModel.IsInputOpen && !Field<InputWindow>(window, "_inputWindow").IsVisible, "full screen closes an already open input window");
            var startup = new PetWindow(viewModel, database, settings, log, () => { }, () => { });
            try
            {
                startup.Show();
                await WaitUntil(() => Field<bool>(startup, "_playInitialized"));
                Check(Field<bool>(startup, "_isFullScreenSuppressed") && !Field<Grid>(startup, "PetRoot").IsVisible,
                    "startup during full screen remains silent");
            }
            finally { startup.AllowClose(); startup.Close(); }
            await Mode("minimized");
            await WaitUntil(() => !Field<bool>(window, "_isFullScreenSuppressed"));
            Check(!UserActivityHelper.IsOtherApplicationFullScreen(), "minimized full-screen window does not block the desktop");
            window.RegisterExternalInteraction();
        }
        finally
        {
            if (!host.HasExited)
            {
                await host.StandardInput.WriteLineAsync("exit");
                await host.StandardInput.FlushAsync();
                await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static int RunFullScreenHost(Application app)
    {
        var window = new Window
        {
            Title = "绵绵全屏避让回归测试", Width = 520, Height = 260,
            Background = new SolidColorBrush(Color.FromRgb(35, 39, 48)),
            Content = new TextBlock { Text = "正在验证全屏自动避让…", Foreground = Brushes.White,
                FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        window.Show();
        var handle = new WindowInteropHelper(window).Handle;
        Console.WriteLine("ready");
        // Bound the helper lifetime even if the parent test fails.
        var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        deadline.Tick += (_, _) => app.Shutdown();
        deadline.Start();
        _ = Task.Run(async () =>
        {
            while (await Console.In.ReadLineAsync() is { } mode)
            {
                await app.Dispatcher.InvokeAsync(() =>
                {
                    if (mode == "exit") { app.Shutdown(); return; }
                    window.WindowState = WindowState.Normal;
                    window.WindowStyle = mode == "fullscreen" ? WindowStyle.None : WindowStyle.SingleBorderWindow;
                    window.ResizeMode = mode == "fullscreen" ? ResizeMode.NoResize : ResizeMode.CanResize;
                    if (mode == "fullscreen")
                    {
                        var bounds = System.Windows.Forms.Screen.FromHandle(handle).Bounds;
                        SetWindowPos(handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040);
                    }
                    else if (mode == "maximized") window.WindowState = WindowState.Maximized;
                    else if (mode == "minimized") window.WindowState = WindowState.Minimized;
                    else { window.Left = 200; window.Top = 150; window.Width = 520; window.Height = 260; }
                    if (mode != "minimized")
                    {
                        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                        var currentThread = GetCurrentThreadId();
                        var attached = AttachThreadInput(currentThread, foregroundThread, true);
                        try { window.Activate(); SetForegroundWindow(handle); }
                        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
                    }
                });
                if (mode == "exit") return;
                await Task.Delay(100);
                Console.WriteLine(mode == "minimized" || GetForegroundWindow() == handle
                    ? mode : $"{mode}: could not activate test window");
            }
            await app.Dispatcher.InvokeAsync(() => app.Shutdown());
        });
        app.Run();
        return 0;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint thread, uint otherThread, bool attach);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using MianMianPet.Animation;
using MianMianPet.Infrastructure;
using MianMianPet.Models;
using MianMianPet.Services;
using MianMianPet.ViewModels;
using MianMianPet.Views;

namespace MianMianPet.Tests;

// Dependency-free Windows/STA regression runner: dotnet run --project MianMianPet.Tests
internal static partial class Program
{
    private static readonly string Output = Path.GetFullPath(Path.Combine("TestResults", "play-v2.2.1"));
    private static int _assertions;

    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(Output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Contains("--fullscreen-host")) return RunFullScreenHost(app);
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = new XElement(ns + "ResourceDictionary",
            source.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration),
            source.Root.Element(ns + "Application.Resources")!.Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString().Replace(
            "clr-namespace:MianMianPet.Infrastructure", "clr-namespace:MianMianPet.Infrastructure;assembly=MianMianPet"));
        return RunWithDispatcher(app, args);
    }

    private static int RunWithDispatcher(Application app, string[] args)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var frame = new DispatcherFrame();
        var task = RunAsync(args);
        _ = task.ContinueWith(_ => app.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        try
        {
            task.GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {_assertions} assertions; renders: {Output}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally { app.Shutdown(); }
    }

    private static async Task RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Contains("--config-only"))
            await ConfigTests.RunAsync(Path.Combine("TestResults", "config-v2.1.1"), Check);
        if (args.Contains("--config-only")) return;
        if (args.Length == 0 || args.Contains("--services-only"))
            await ServiceRegressionTests.RunAsync(Path.Combine("TestResults", "services-v2.2.0"), Check);
        if (args.Contains("--services-only")) return;
        if (args.Length == 0 || args.Contains("--settings-only"))
            await SettingsRegressionTests.RunAsync(Path.Combine("TestResults", "settings-v2.2.0"), Check);
        if (args.Contains("--settings-only")) return;
        if (args.Length == 0 || args.Contains("--core-only"))
            await CoreRegressionTests.RunAsync(Path.Combine("TestResults", "core-v2.2.0"), Check);
        if (args.Contains("--core-only")) return;
        var restartCheck = args.Length == 2 && args[0] == "--restart-check";
        if (args.Length == 0) TestDeadlines();
        var paths = new AppPaths(restartCheck ? args[1] : Path.Combine(Output, Guid.NewGuid().ToString("N")));
        var log = new LogService(paths);
        var database = new DatabaseService(paths, log);
        await database.InitializeAsync();
        var settings = new SettingsService(paths, log);
        using var client = new HttpClient();
        using var viewModel = new PetViewModel(new DeepSeekClient(client, settings, new PetPersona("test")),
            new ConversationService(database), settings, log, new CelebrationService(database, paths));
        await viewModel.InitializeAsync();
        var window = new PetWindow(viewModel, database, settings, log, () => { }, () => { });
        if (restartCheck || args.Contains("--window-only") || args.Contains("--play-only")) window.FullScreenDetector = () => false;
        window.Show();
        await WaitUntil(() => Field<bool>(window, "_playInitialized"));
        window.Left = 600;
        window.Top = 400;
        var session = Field<PetPlaySession>(window, "_play");
        var panel = Field<PetPlayPanel>(window, "PlayPanel");
        var artwork = Field<PetVisual>(window, "PetArtwork");
        var popup = Field<Popup>(window, "PlayPopup");
        Window? foreground = null;
        try
        {
            if (restartCheck)
            {
                Check(!session.IsActive && session.Treasure is null && session.HiddenCloud == -1, "new process starts idle");
                Check(!popup.IsOpen && !Field<System.Windows.Shapes.Path>(artwork, "TreasureAdornment").IsVisible, "new process restores no temporary visuals");
                Check(Field<PetPlayKind?>(window, "_lastPlayKind") is not null, "new process reads SQLite activity history");
                return;
            }
            if (args.Contains("--preview"))
            {
                await PreviewAsync(window);
                return;
            }
            if (args.Contains("--window-only"))
            {
                await WindowRegressionTests.RunAsync(window, viewModel, Check);
                return;
            }
            if (!args.Contains("--play-only"))
                await TestFullScreenAsync(window, viewModel, database, settings, log);
            if (args.Contains("--fullscreen-only")) return;
            // Native fullscreen detection is verified above; other tests isolate external desktop changes.
            window.FullScreenDetector = () => false;
            foreground = WindowRegressionTests.CreateForegroundWindow();
            if (!args.Contains("--play-only"))
                await WindowRegressionTests.RunAsync(window, viewModel, Check, foreground);
            // Use real WPF routed Click events, real timers and a private SQLite database.
            var petHitArea = Field<Border>(window, "PetHitArea");
            var speech = Field<Grid>(panel, "Speech");
            var clouds = Field<Canvas>(panel, "GameStage");
            Start(window, PetPlayKind.Walk);
            Check(session.Phase == PetPlayPhase.WalkGreeting && popup.IsOpen && speech.IsVisible && petHitArea.IsVisible,
                "walk starts with a speech cloud while the pet is visible");
            await Task.Delay(240);
            Render(panel, "walk-greeting");
            await WaitUntil(() => session.Phase == PetPlayPhase.Walking);
            var frontStride = Field<RotateTransform>(artwork, "FrontStride");
            var backStride = Field<RotateTransform>(artwork, "BackStride");
            await WaitUntil(() => Math.Abs(frontStride.Angle) > 6, 700);
            Check(!popup.IsOpen && frontStride.Angle * backStride.Angle < 0, "front and back legs step in opposite directions");
            Render(window, "walk-step");
            await WaitUntil(() => !petHitArea.IsVisible);
            await Task.Delay(80);
            Check(!frontStride.HasAnimatedProperties && !backStride.HasAnimatedProperties, "walking stops once the pet is out of sight");
            await WaitUntil(() => session.Phase == PetPlayPhase.Returning);
            await WaitUntil(() => petHitArea.Opacity is > 0.1 and < 0.9, 700);
            Check(petHitArea.IsVisible && !popup.IsOpen, "pet fades in before the return speech");
            Render(window, "walk-reappearing");
            await WaitUntil(() => session.Phase == PetPlayPhase.ShowingTreasure, diagnostics:
                () => $"phase={session.Phase}, suppressed={Field<bool>(window, "_isFullScreenSuppressed")}, visible={window.IsVisible}, frameTimer={Field<DispatcherTimer>(window, "_playFrameTimer").IsEnabled}");
            var treasureDeadline = session.Deadline;
            Check(popup.IsOpen && speech.IsVisible && Field<System.Windows.Shapes.Path>(artwork, "TreasureAdornment").IsVisible,
                "returned pet wears the treasure and speaks in a cloud");
            Check(await database.GetStateAsync("play.last") == "walk:returned", "walk outcome saved");
            await Task.Delay((int)Math.Max(0, treasureDeadline - Environment.TickCount64 - 1000));
            Check(session.Phase == PetPlayPhase.ShowingTreasure,
                $"treasure stays visible before its deadline (phase={session.Phase}, remaining={treasureDeadline - Environment.TickCount64}ms, suppressed={Field<bool>(window, "_isFullScreenSuppressed")}, visible={window.IsVisible})");
            Check(speech.Opacity > 0.99 && Field<Grid>(panel, "SpeechContent").Opacity > 0.99,
                "return speech fades in after the greeting has faded out");
            // Export after the timing assertion so PNG encoding cannot consume its waiting margin.
            Render(panel, "walk-returned");
            Render(artwork, "treasure-worn");
            await WaitUntil(() => !session.IsActive);
            Check(Environment.TickCount64 >= treasureDeadline && Environment.TickCount64 - treasureDeadline < 5000,
                "real display expires after its seven-second deadline");
            Check(!popup.IsOpen && !Field<System.Windows.Shapes.Path>(artwork, "TreasureAdornment").IsVisible, "treasure and panel expire together");

            Start(window, PetPlayKind.HideAndSeek);
            var hidden = session.HiddenCloud;
            ClickCloud(panel, hidden);
            Check(session.Phase == PetPlayPhase.HideGreeting && speech.IsVisible && !clouds.IsVisible,
                "hide-and-seek announces itself before the choices can be clicked");
            await Task.Delay(240);
            Render(panel, "hide-greeting");
            await WaitUntil(() => session.Phase == PetPlayPhase.Hiding);
            Check(clouds.IsVisible && !speech.IsVisible && !petHitArea.IsVisible &&
                Enumerable.Range(0, 3).All(index => Cloud(panel, index).Content is null),
                "the guessing phase shows three unnumbered clouds with no text");
            for (var offset = 1; offset <= 2; offset++)
            {
                var index = (hidden + offset) % 3;
                ClickCloud(panel, index);
                var wrong = Cloud(panel, index);
                var body = (FrameworkElement)wrong.Template.FindName("CloudBody", wrong);
                Check(session.Phase == PetPlayPhase.Hiding && body.RenderTransform is TranslateTransform { HasAnimatedProperties: true },
                    "wrong cloud shakes and keeps the game active");
            }
            var button = Cloud(panel, hidden);
            await WaitUntil(() => button.Template.FindName("PeekEars", button) is UIElement { IsVisible: true });
            await Task.Delay(190);
            Check(Enumerable.Range(0, 3).Where(index => index != hidden).All(index =>
            {
                var other = Cloud(panel, index);
                return other.Template.FindName("PeekEars", other) is UIElement { IsVisible: false };
            }), "only hidden cloud peeks");
            Render(panel, "hide-peeking");
            ClickCloud(panel, hidden);
            Check(session.Phase == PetPlayPhase.Found, "correct routed click wins");
            Check(!speech.IsVisible, "the reveal uses motion without a text card");
            ClickCloud(panel, hidden);
            Check(session.Phase == PetPlayPhase.Found, "duplicate click cannot restart reveal");
            await Task.Delay(700);
            Check(Field<PetVisual>(panel, "RevealPet").IsVisible, "winning pet jumps out");
            Render(panel, "hide-found");
            await WaitUntil(() => !session.IsActive);
            Check(await database.GetStateAsync("play.last") == "hide:found", "win saved");

            Start(window, PetPlayKind.HideAndSeek);
            await WaitUntil(() => session.Phase == PetPlayPhase.TimedOut, 13000,
                () => $"phase={session.Phase}, remaining={session.Deadline - Environment.TickCount64}ms, suppressed={Field<bool>(window, "_isFullScreenSuppressed")}, visible={window.IsVisible}, frameTimer={Field<DispatcherTimer>(window, "_playFrameTimer").IsEnabled}");
            Check(Field<PetVisual>(panel, "RevealPet").IsVisible, "timeout reveals pet");
            await Task.Delay(600);
            Render(panel, "hide-timeout");
            await WaitUntil(() => !session.IsActive);
            Check(await database.GetStateAsync("play.last") == "hide:timeout", "timeout saved");
            Check(Field<Border>(window, "PetHitArea").IsHitTestVisible, "timeout restores interaction");

            Start(window, PetPlayKind.Walk);
            window.RegisterExternalInteraction();
            Check(!session.IsActive && !popup.IsOpen, "settings/external interaction cancels walk");
            Start(window, PetPlayKind.HideAndSeek);
            window.Hide();
            Check(!session.IsActive && !popup.IsOpen, "hiding window clears game");
            Check(!Field<DispatcherTimer>(window, "_playFrameTimer").IsEnabled, "hidden window stops timer");
            window.Show();
            viewModel.ToggleInput();
            SetField(window, "_playIdleSince", Environment.TickCount64 - 80000);
            Check(!TryIdle(window, TimeSpan.Zero), "open chat blocks automatic game");
            viewModel.HideInput();
            window.RegisterExternalInteraction();
            Check(!TryIdle(window, TimeSpan.Zero), "recent pet interaction blocks game");
            SetField(window, "_playIdleSince", Environment.TickCount64 - 80000);
            Check(!TryIdle(window, TimeSpan.FromMinutes(3)), "away user blocks game");
            SetField(window, "_isPointerDown", true);
            Check(!TryIdle(window, TimeSpan.Zero), "dragging blocks game");
            SetField(window, "_isPointerDown", false);
            SetField(window, "_lastPlayKind", PetPlayKind.Walk);
            Check(TryIdle(window, TimeSpan.Zero) && session.Phase == PetPlayPhase.HideGreeting, "idle scheduler alternates walk to hiding");
            window.RegisterExternalInteraction();
            SetField(window, "_playIdleSince", Environment.TickCount64 - 80000);
            SetField(window, "_lastPlayKind", PetPlayKind.HideAndSeek);
            Check(TryIdle(window, TimeSpan.Zero) && session.Phase == PetPlayPhase.WalkGreeting, "idle scheduler alternates hiding to walk");
            window.RegisterExternalInteraction();

            // Recreate windows against the same database at each transient phase.
            foreach (var phase in new[] { PetPlayPhase.Walking, PetPlayPhase.ShowingTreasure, PetPlayPhase.Hiding })
            {
                Start(window, phase == PetPlayPhase.Hiding ? PetPlayKind.HideAndSeek : PetPlayKind.Walk);
                await WaitUntil(() => session.Phase == phase, 8500);
                window.AllowClose();
                window.Close();
                var processInfo = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                processInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
                processInfo.ArgumentList.Add("--restart-check");
                processInfo.ArgumentList.Add(paths.Root);
                using var restarted = Process.Start(processInfo)!;
                var output = restarted.StandardOutput.ReadToEndAsync();
                var errors = restarted.StandardError.ReadToEndAsync();
                await restarted.WaitForExitAsync();
                Check(restarted.ExitCode == 0, $"separate process restart from {phase}: {await errors}");
                Console.Write(await output);
                window = new PetWindow(viewModel, database, settings, log, () => { }, () => { });
                window.FullScreenDetector = () => false;
                window.Show();
                await WaitUntil(() => Field<bool>(window, "_playInitialized"));
                session = Field<PetPlaySession>(window, "_play");
                Check(!session.IsActive && session.Treasure is null && session.HiddenCloud == -1, $"restart clears {phase}");
                Check(!Field<Popup>(window, "PlayPopup").IsOpen, "restart has no old popup");
                Check(!Field<System.Windows.Shapes.Path>(Field<PetVisual>(window, "PetArtwork"), "TreasureAdornment").IsVisible, "restart has no accessory");
            }

            await using var connection = new SqliteConnection($"Data Source={paths.DatabaseFile}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM app_state WHERE key LIKE 'play.%';";
            Check((long)(await command.ExecuteScalarAsync())! == 1, "SQLite stores only last outcome");
            command.CommandText = "SELECT COUNT(*) FROM conversations;";
            Check((long)(await command.ExecuteScalarAsync())! == 0, "games do not pollute AI conversation history");

            panel = Field<PetPlayPanel>(window, "PlayPanel");
            artwork = Field<PetVisual>(window, "PetArtwork");
            var work = SystemParameters.WorkArea;
            foreach (var corner in new[]
            {
                new Point(work.Left, work.Top), new Point(work.Right - window.Width, work.Top),
                new Point(work.Left, work.Bottom - window.Height), new Point(work.Right - window.Width, work.Bottom - window.Height)
            })
            {
                window.Left = corner.X;
                window.Top = corner.Y;
                Start(window, PetPlayKind.Walk);
                await Task.Delay(100);
                var position = panel.PointToScreen(new Point());
                var dpi = VisualTreeHelper.GetDpi(window);
                Check(position.X >= work.Left * dpi.DpiScaleX && position.Y >= work.Top * dpi.DpiScaleY &&
                    position.X + panel.ActualWidth * dpi.DpiScaleX <= work.Right * dpi.DpiScaleX &&
                    position.Y + panel.ActualHeight * dpi.DpiScaleY <= work.Bottom * dpi.DpiScaleY, "game panel stays on screen at corner");
                window.RegisterExternalInteraction();
            }
            for (var i = 0; i < WalkTreasures.All.Count; i++)
            {
                panel.ShowSpeech(WalkTreasures.All[i].Encounter, PetPlaySession.TreasureMilliseconds);
                artwork.SetTreasure(WalkTreasures.All[i]);
                Render(panel, $"treasure-{i + 1}");
                Render(artwork, $"worn-{i + 1}");
            }
            artwork.SetTreasure(null);
        }
        finally { window.AllowClose(); window.Close(); foreground?.Close(); }
    }

    private static async Task PreviewAsync(PetWindow window)
    {
        window.ShowInTaskbar = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20) };
        foreach (var (kind, name) in new[] { (PetPlayKind.Walk, "散步"), (PetPlayKind.HideAndSeek, "捉迷藏") })
        {
            var button = new Button { Content = name, Margin = new Thickness(5), Padding = new Thickness(18, 8, 18, 8) };
            button.Click += (_, _) => { window.RegisterExternalInteraction(); Start(window, kind); };
            buttons.Children.Add(button);
        }
        var controls = new Window
        {
            Title = "绵绵玩法验收", Width = 370, Height = 130, Left = 500, Top = 100,
            Content = buttons, ResizeMode = ResizeMode.NoResize
        };
        var closed = new TaskCompletionSource();
        controls.Closed += (_, _) => closed.TrySetResult();
        controls.Show();
        await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromMinutes(3)));
        controls.Close();
    }

    private static void TestDeadlines()
    {
        Check(WalkTreasures.All.Count == 6 && WalkTreasures.All.Select(t => t.Name).Distinct().Count() == 6, "six distinct treasures");
        foreach (var treasure in WalkTreasures.All)
        {
            Check(!Geometry.Parse(treasure.Shape).IsEmpty(), "treasure artwork parses");
            var game = new PetPlaySession();
            game.StartWalk(treasure, 100);
            var departAt = 100 + PetPlaySession.GreetingMilliseconds;
            Check(game.Phase == PetPlayPhase.WalkGreeting && game.Deadline == departAt, "walk opens with a short greeting");
            game.Advance(departAt);
            var returnAt = departAt + PetPlaySession.WalkMilliseconds;
            game.Advance(returnAt - 1);
            Check(game.Phase == PetPlayPhase.Walking, "walk lasts four seconds");
            game.Advance(returnAt);
            Check(game.Phase == PetPlayPhase.Returning, "pet reappears before it speaks");
            var speakAt = returnAt + PetPlaySession.ReturnMilliseconds;
            game.Advance(speakAt);
            Check(game.Phase == PetPlayPhase.ShowingTreasure && game.Treasure == treasure, "walk returns chosen treasure");
            game.Advance(speakAt + 6999);
            Check(game.Phase == PetPlayPhase.ShowingTreasure, "not removed early");
            game.Advance(speakAt + 7000);
            Check(!game.IsActive && game.Treasure is null, "removed exactly at seven seconds");
        }
        for (var cloud = 0; cloud < 3; cloud++)
        {
            var game = new PetPlaySession();
            var hideAt = PetPlaySession.GreetingMilliseconds;
            var deadline = hideAt + 10000;
            game.StartHiding(cloud, 0);
            Check(!game.ChooseCloud(cloud, hideAt - 1) && game.Phase == PetPlayPhase.HideGreeting, "the opening speech cannot be guessed");
            game.Advance(hideAt);
            Check(!game.ChooseCloud((cloud + 1) % 3, hideAt + 500), "wrong click does not win");
            Check(game.Deadline == deadline, "the choices stay open for ten seconds and wrong clicks do not extend it");
            Check(game.ChooseCloud(cloud, deadline - 1), "click before deadline wins");
            game.Advance(deadline - 1 + PetPlaySession.RevealMilliseconds);
            Check(!game.IsActive, "win finishes");
            game.StartHiding(cloud, 0);
            game.Advance(hideAt);
            Check(!game.ChooseCloud(cloud, deadline) && game.Phase == PetPlayPhase.TimedOut, "click at deadline times out without timer tick");
            game.Reset();
            game.StartHiding(cloud, 0);
            game.Advance(hideAt);
            var lateClick = deadline + 5000;
            Check(!game.ChooseCloud(cloud, lateClick) && game.Phase == PetPlayPhase.TimedOut, "late click after UI stall times out");
            game.Advance(lateClick + PetPlaySession.RevealMilliseconds);
            Check(!game.IsActive && game.HiddenCloud == -1, "timeout clears hidden cloud");
        }
        var stalled = new PetPlaySession();
        stalled.StartWalk(WalkTreasures.All[0], 0);
        stalled.Advance(stalled.Deadline);
        stalled.Advance(stalled.Deadline);
        stalled.Advance(30000);
        Check(stalled.Deadline == 37000, "late return speech still displays for full seven seconds");
        stalled.Reset();
        Check(stalled.Treasure is null && !stalled.IsActive, "cancellation clears temporary treasure");
    }

    private static void Start(PetWindow window, PetPlayKind kind) => Call(window, "StartPlay", kind);
    private static Button Cloud(PetPlayPanel panel, int cloud) => Field<Button>(panel, new[] { "CloudOne", "CloudTwo", "CloudThree" }[cloud]);
    private static void ClickCloud(PetPlayPanel panel, int cloud) => Cloud(panel, cloud).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static bool TryIdle(PetWindow window, TimeSpan systemIdle) => (bool)typeof(PetWindow).GetMethod("TryStartIdlePlay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [systemIdle])!;
    private static void Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {message}");
        _assertions++;
        Console.WriteLine($"PASS: {message}");
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000, Func<string>? diagnostics = null)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds >= timeoutMs)
                throw new TimeoutException($"Expected UI transition did not arrive. {diagnostics?.Invoke()}");
            await Task.Delay(25);
        }
    }

    internal static void Render(FrameworkElement element, string name)
    {
        if (!element.IsVisible)
        {
            var size = new Size(element.Width, element.Height);
            element.Measure(size);
            element.Arrange(new Rect(size));
        }
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth * 2, (int)element.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(Output, name + ".png"));
        encoder.Save(output);
    }
}

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
    private static readonly string Output = Path.GetFullPath(Path.Combine("TestResults", "play-v2.1.2"));
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
        window.Show();
        await WaitUntil(() => Field<bool>(window, "_playInitialized"));
        window.Left = 600;
        window.Top = 400;
        var session = Field<PetPlaySession>(window, "_play");
        var panel = Field<PetPlayPanel>(window, "PlayPanel");
        var artwork = Field<PetVisual>(window, "PetArtwork");
        var popup = Field<Popup>(window, "PlayPopup");
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
            await TestFullScreenAsync(window, viewModel, database, settings, log);
            if (args.Contains("--fullscreen-only")) return;
            // Use real WPF routed Click events, real timers and a private SQLite database.
            Start(window, PetPlayKind.Walk);
            Check(session.Phase == PetPlayPhase.Walking && popup.IsOpen, "walk opens cloud panel");
            await Task.Delay(450);
            Check(Field<Border>(window, "PetHitArea").Opacity == 0, "pet leaves during walk");
            Render(panel, "walk-away");
            await WaitUntil(() => session.Phase == PetPlayPhase.ShowingTreasure);
            var shownAt = Environment.TickCount64;
            Check(Field<System.Windows.Shapes.Path>(artwork, "TreasureAdornment").IsVisible, "returned treasure is worn");
            Check(await database.GetStateAsync("play.last") == "walk:returned", "walk outcome saved");
            Render(panel, "walk-returned");
            Render(artwork, "treasure-worn");
            await Task.Delay(6100);
            Check(session.Phase == PetPlayPhase.ShowingTreasure, "treasure stays visible until seven seconds");
            await WaitUntil(() => !session.IsActive);
            Check(Environment.TickCount64 - shownAt is >= 6900 and < 7700, "real display lasts seven seconds");
            Check(!popup.IsOpen && !Field<System.Windows.Shapes.Path>(artwork, "TreasureAdornment").IsVisible, "treasure and panel expire together");

            Start(window, PetPlayKind.HideAndSeek);
            var hidden = session.HiddenCloud;
            ClickCloud(panel, (hidden + 1) % 3);
            Check(session.Phase == PetPlayPhase.Hiding, "wrong cloud keeps game active");
            await Task.Delay(2300);
            var button = Cloud(panel, hidden);
            Check(((UIElement)button.Template.FindName("PeekEars", button)).Visibility == Visibility.Visible, "only hidden cloud peeks");
            Render(panel, "hide-peeking");
            ClickCloud(panel, hidden);
            Check(session.Phase == PetPlayPhase.Found, "correct routed click wins");
            ClickCloud(panel, hidden);
            Check(session.Phase == PetPlayPhase.Found, "duplicate click cannot restart reveal");
            await Task.Delay(700);
            Check(Field<PetVisual>(panel, "RevealPet").IsVisible, "winning pet jumps out");
            Render(panel, "hide-found");
            await WaitUntil(() => !session.IsActive);
            Check(await database.GetStateAsync("play.last") == "hide:found", "win saved");

            Start(window, PetPlayKind.HideAndSeek);
            await WaitUntil(() => session.Phase == PetPlayPhase.TimedOut, 11000);
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
            Check(TryIdle(window, TimeSpan.Zero) && session.Phase == PetPlayPhase.Hiding, "idle scheduler alternates walk to hiding");
            window.RegisterExternalInteraction();
            SetField(window, "_playIdleSince", Environment.TickCount64 - 80000);
            SetField(window, "_lastPlayKind", PetPlayKind.HideAndSeek);
            Check(TryIdle(window, TimeSpan.Zero) && session.Phase == PetPlayPhase.Walking, "idle scheduler alternates hiding to walk");
            window.RegisterExternalInteraction();

            // Recreate windows against the same database at each transient phase.
            foreach (var phase in new[] { PetPlayPhase.Walking, PetPlayPhase.ShowingTreasure, PetPlayPhase.Hiding })
            {
                Start(window, phase == PetPlayPhase.Hiding ? PetPlayKind.HideAndSeek : PetPlayKind.Walk);
                if (phase == PetPlayPhase.ShowingTreasure)
                    await WaitUntil(() => session.Phase == PetPlayPhase.ShowingTreasure);
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
                panel.ShowWalking();
                panel.ShowTreasure(WalkTreasures.All[i]);
                artwork.SetTreasure(WalkTreasures.All[i]);
                Render(panel, $"treasure-{i + 1}");
                Render(artwork, $"worn-{i + 1}");
            }
            artwork.SetTreasure(null);
        }
        finally { window.AllowClose(); window.Close(); }
    }

    private static async Task PreviewAsync(PetWindow window)
    {
        window.ShowInTaskbar = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20) };
        foreach (var (kind, name) in new[] { (PetPlayKind.Walk, "散步寻宝"), (PetPlayKind.HideAndSeek, "十秒捉迷藏") })
        {
            var button = new Button { Content = name, Margin = new Thickness(5), Padding = new Thickness(18, 8, 18, 8) };
            button.Click += (_, _) => { window.RegisterExternalInteraction(); Start(window, kind); };
            buttons.Children.Add(button);
        }
        var controls = new Window
        {
            Title = "绵绵 v2.1.2 玩法验收", Width = 370, Height = 130, Left = 500, Top = 100,
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
            game.Advance(4099);
            Check(game.Phase == PetPlayPhase.Walking, "walk lasts four seconds");
            game.Advance(4100);
            Check(game.Phase == PetPlayPhase.ShowingTreasure && game.Treasure == treasure, "walk returns chosen treasure");
            game.Advance(11099);
            Check(game.Phase == PetPlayPhase.ShowingTreasure, "not removed early");
            game.Advance(11100);
            Check(!game.IsActive && game.Treasure is null, "removed exactly at seven seconds");
        }
        for (var cloud = 0; cloud < 3; cloud++)
        {
            var game = new PetPlaySession();
            game.StartHiding(cloud, 0);
            Check(!game.ChooseCloud((cloud + 1) % 3, 500), "wrong click does not win");
            Check(game.Deadline == 10000, "wrong click does not extend deadline");
            Check(game.ChooseCloud(cloud, 9999), "click before deadline wins");
            game.Advance(11799);
            Check(!game.IsActive, "win finishes");
            game.StartHiding(cloud, 0);
            Check(!game.ChooseCloud(cloud, 10000) && game.Phase == PetPlayPhase.TimedOut, "click at deadline times out without timer tick");
            game.Reset();
            game.StartHiding(cloud, 0);
            Check(!game.ChooseCloud(cloud, 15000) && game.Phase == PetPlayPhase.TimedOut, "late click after UI stall times out");
            game.Advance(16800);
            Check(!game.IsActive && game.HiddenCloud == -1, "timeout clears hidden cloud");
        }
        var stalled = new PetPlaySession();
        stalled.StartWalk(WalkTreasures.All[0], 0);
        stalled.Advance(30000);
        Check(stalled.Deadline == 37000, "late return still displays for full seven seconds");
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

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds >= timeoutMs) throw new TimeoutException("Expected UI transition did not arrive.");
            await Task.Delay(25);
        }
    }

    private static void Render(FrameworkElement element, string name)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth * 2, (int)element.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(Output, name + ".png"));
        encoder.Save(output);
    }
}

using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using MianMianPet.Services;

namespace MianMianPet.Infrastructure;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly LogService? _log;
    private readonly TimeSpan _readTimeout;
    private CancellationTokenSource? _serverCancellation;
    private bool _disposed;

    public SingleInstanceGuard(LogService? log = null) : this(log, string.Empty, TimeSpan.FromSeconds(2)) { }

    internal SingleInstanceGuard(LogService? log, string suffix, TimeSpan readTimeout)
    {
        _log = log;
        _readTimeout = readTimeout;
        var scope = GetScope(suffix);
        PipeName = $"MianMianPet.Activate.{scope}";
        _mutex = new Mutex(true, $"Local\\MianMianPet.SingleInstance.{scope}", out var createdNew);
        IsPrimary = createdNew;
    }

    internal string PipeName { get; }
    public bool IsPrimary { get; }

    public event EventHandler? ActivationRequested;

    private static string GetScope(string suffix)
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{identity.User?.Value}:{process.SessionId}:{suffix}")))[..24];
    }

    public void StartListening()
    {
        if (!IsPrimary || _serverCancellation is not null || _disposed)
        {
            return;
        }

        _serverCancellation = new CancellationTokenSource();
        _ = ListenLoopAsync(_serverCancellation.Token);
    }

    public static async Task NotifyPrimaryAsync(LogService? log = null)
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".", $"MianMianPet.Activate.{GetScope(string.Empty)}", PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("SHOW".AsMemory(), timeout.Token);
        }
        catch (Exception ex)
        {
            if (log is not null) await log.ErrorAsync("activation_notify_failed", ex);
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_readTimeout);
                using var reader = new StreamReader(server);
                var buffer = new char[16];
                var length = 0;
                while (length < buffer.Length)
                {
                    if (await reader.ReadAsync(buffer.AsMemory(length, 1), timeout.Token) == 0) break;
                    if (buffer[length++] != '\n') continue;
                    if (new string(buffer, 0, length).TrimEnd('\r', '\n') == "SHOW")
                        ActivationRequested?.Invoke(this, EventArgs.Empty);
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException ex)
            {
                if (_log is not null) await _log.ErrorAsync("activation_read_timed_out", ex);
            }
            catch (Exception ex)
            {
                if (_log is not null) await _log.ErrorAsync("activation_listen_failed", ex);
                try { await Task.Delay(500, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _serverCancellation?.Cancel();
        _serverCancellation?.Dispose();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}

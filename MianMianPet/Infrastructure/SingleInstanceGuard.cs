using System.IO.Pipes;

namespace MianMianPet.Infrastructure;

public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Local\\MianMianPet.SingleInstance.5F12C1B3";
    private const string PipeName = "MianMianPet.Activate.5F12C1B3";
    private readonly Mutex _mutex;
    private CancellationTokenSource? _serverCancellation;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public event EventHandler? ActivationRequested;

    public void StartListening()
    {
        if (!IsPrimary || _serverCancellation is not null)
        {
            return;
        }

        _serverCancellation = new CancellationTokenSource();
        _ = ListenLoopAsync(_serverCancellation.Token);
    }

    public static async Task NotifyPrimaryAsync()
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync("SHOW");
        }
        catch
        {
            // The primary process may still be initializing; a second pet is still prevented by the mutex.
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
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync(cancellationToken);
                if (string.Equals(message, "SHOW", StringComparison.Ordinal))
                {
                    ActivationRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _serverCancellation?.Cancel();
        _serverCancellation?.Dispose();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}

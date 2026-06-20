using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace UIMarkerEditor;

internal sealed class SingleInstanceService : IDisposable
{
    private const string InstanceName = "FFXIVConfigEditor.UIMarkerEditor.SingleInstance.v1";
    private const string MutexName = "Local\\" + InstanceName;
    private const string PipeName = InstanceName;
    private const string ActivateMessage = "ActivateMainWindow";

    private readonly Mutex mutex;
    private readonly bool ownsMutex;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private Task? listenerTask;
    private bool disposed;

    private SingleInstanceService(Mutex mutex, bool ownsMutex)
    {
        this.mutex = mutex;
        this.ownsMutex = ownsMutex;
    }

    public bool IsFirstInstance => ownsMutex;

    public static SingleInstanceService Create()
    {
        Mutex mutex = new(true, MutexName, out bool createdNew);
        return new SingleInstanceService(mutex, createdNew);
    }

    public static bool NotifyFirstInstance()
    {
        try
        {
            using NamedPipeClientStream client = new(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.None);
            client.Connect(500);

            using StreamWriter writer = new(client);
            writer.WriteLine(ActivateMessage);
            writer.Flush();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void StartActivationListener(Action activationRequested, Action<Exception> logError)
    {
        if (!ownsMutex || listenerTask != null)
        {
            return;
        }

        listenerTask = Task.Run(() => ListenForActivationRequestsAsync(
            activationRequested,
            logError,
            cancellationTokenSource.Token));
    }

    private static async Task ListenForActivationRequestsAsync(
        Action activationRequested,
        Action<Exception> logError,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream server = new(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using StreamReader reader = new(server);
                string? message = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.Equals(message, ActivateMessage, StringComparison.Ordinal))
                {
                    activationRequested();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logError(ex);
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();

        if (ownsMutex)
        {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
    }
}

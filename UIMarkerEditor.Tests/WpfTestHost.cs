using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace UIMarkerEditor.Tests;

internal static class WpfTestHost
{
    private static readonly object SyncRoot = new();
    private static readonly object RunSyncRoot = new();
    private static Dispatcher? dispatcher;
    private static Exception? startupException;
    private static bool isStarting;

    public static Exception? Run(Action action)
    {
        EnsureStarted();
        if (startupException != null)
        {
            return startupException;
        }

        lock (RunSyncRoot)
        {
            Exception? exception = null;
            dispatcher!.Invoke(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            return exception;
        }
    }

    public static void EnsureApplicationResources()
    {
        Application application = Application.Current ?? throw new InvalidOperationException("WPF Application has not been initialized.");

        application.Resources.MergedDictionaries.Clear();
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/UIMarkerEditor;component/Styles/Theme.xaml", UriKind.Absolute)
        });
    }

    private static void EnsureStarted()
    {
        lock (SyncRoot)
        {
            if (dispatcher != null || startupException != null)
            {
                return;
            }

            if (!isStarting)
            {
                isStarting = true;
                Thread thread = new(StartDispatcher)
                {
                    IsBackground = true
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }

            while (dispatcher == null && startupException == null)
            {
                Monitor.Wait(SyncRoot);
            }
        }
    }

    private static void StartDispatcher()
    {
        try
        {
            _ = Application.Current ?? new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            Dispatcher currentDispatcher = Dispatcher.CurrentDispatcher;
            lock (SyncRoot)
            {
                dispatcher = currentDispatcher;
                Monitor.PulseAll(SyncRoot);
            }

            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            lock (SyncRoot)
            {
                startupException = ex;
                Monitor.PulseAll(SyncRoot);
            }
        }
    }
}

using System.Windows;
using System.Windows.Threading;

namespace ChatPi.Windows.Tests;

internal static class WpfTestHost
{
    private static readonly object Gate = new();
    private static readonly TaskCompletionSource<Dispatcher> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    static WpfTestHost()
    {
        var thread = new Thread(() =>
        {
            try
            {
                var app = new ChatPi.App.App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception) { Ready.TrySetException(exception); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static void Run(Action action)
    {
        // Serialize whole tests, including nested dispatcher work used to flush bindings.
        lock (Gate)
        {
            var dispatcher = Ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            dispatcher.InvokeAsync(action).Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        }
    }

    public static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}

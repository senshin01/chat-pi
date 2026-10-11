using System.Security.Principal;
using System.Windows;
using ChatPi.App.Services;
using ChatPi.Core;

namespace ChatPi.App;

public partial class App : Application
{
    private SingleInstanceHost? _instance;

    protected override async void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        try
        {
            var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            _instance = new SingleInstanceHost(user);
            var path = AudioFiles.FirstFileArgument(args.Args);
            if (!_instance.IsPrimary)
            {
                await _instance.SendAsync(path);
                Shutdown();
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            _instance.StartListening(message => Dispatcher.InvokeAsync(() =>
            {
                window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
                if (message is not null) window.OpenFile(message);
            }).Task);
            window.Show();
            if (path is not null) window.OpenFile(path);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Chat Pi を起動できません", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs args)
    {
        _instance?.Dispose();
        base.OnExit(args);
    }
}

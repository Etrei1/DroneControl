using System.Windows;
using System.Windows.Threading;

namespace DroneControl.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

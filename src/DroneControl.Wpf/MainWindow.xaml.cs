using System.ComponentModel;
using System.Windows;
using DroneControl.Wpf.ViewModels;

namespace DroneControl.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _shutdownCompleted;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownCompleted)
            return;

        // Сначала корректно останавливаем циклы отправки и шлём 08 01, потом закрываем окно.
        e.Cancel = true;
        IsEnabled = false;
        try
        {
            await _viewModel.DisposeAsync();
        }
        finally
        {
            _shutdownCompleted = true;
            Close();
        }
    }
}

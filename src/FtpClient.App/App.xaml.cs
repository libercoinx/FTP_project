using System.Windows;
using FtpClient.App.ViewModels;

namespace FtpClient.App;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow?.DataContext is MainViewModel viewModel)
        {
            viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }
}

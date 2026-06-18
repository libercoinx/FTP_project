using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FtpClient.App.Dialogs;
using FtpClient.App.ViewModels;
using FtpClient.Core.Models;
using FtpClient.Infrastructure.Persistence;
using FtpClient.Infrastructure.Logging;
using FtpClient.Infrastructure.Transfers;
using Microsoft.Win32;

namespace FtpClient.App;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private bool _syncingPassword;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_OnLoaded;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_OnLoaded;
        try
        {
            var connectionString = LoadConnectionString();
            var logger = new FileAppLogger();
            var database = new MySqlDatabase(new DatabaseOptions { ConnectionString = connectionString });
            var temporaryMode = false;
            try
            {
                await database.InitializeAsync();
            }
            catch
            {
                temporaryMode = true;
                await logger.ErrorAsync(
                    FtpClient.Core.Interfaces.LogCategory.Database,
                    "MySQL 初始化失败，进入临时模式");
            }

            var siteRepository = temporaryMode ? null : new MySqlSiteRepository(database);
            var transferRepository = temporaryMode ? null : new MySqlTransferRepository(database);
            var transferManager = new TransferManager(transferRepository, 2, logger);
            _viewModel = new MainViewModel(siteRepository, transferManager, temporaryMode, logger);
            _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            DataContext = _viewModel;
            await _viewModel.InitializeAsync();
            PasswordInput.Password = _viewModel.Password;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "初始化失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string LoadConnectionString()
    {
        var environmentValue = Environment.GetEnvironmentVariable("FTPCLIENT_DB_CONNECTION");
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("Database")
            .GetProperty("ConnectionString")
            .GetString()
            ?? throw new InvalidOperationException("数据库连接字符串不能为空。");
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_syncingPassword && _viewModel is not null)
        {
            _viewModel.Password = PasswordInput.Password;
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Password) || _viewModel is null)
        {
            return;
        }

        _syncingPassword = true;
        PasswordInput.Password = _viewModel.Password;
        _syncingPassword = false;
    }

    private async void Connect_OnClick(object sender, RoutedEventArgs e) =>
        await RunAsync(() => RequireViewModel().ConnectAsync());

    private async void Disconnect_OnClick(object sender, RoutedEventArgs e) =>
        await RunAsync(() => RequireViewModel().DisconnectAsync());

    private async void Refresh_OnClick(object sender, RoutedEventArgs e) =>
        await RunAsync(() => RequireViewModel().RefreshAsync());

    private async void Parent_OnClick(object sender, RoutedEventArgs e) =>
        await RunAsync(() => RequireViewModel().GoParentAsync());

    private async void RemoteGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        await RunAsync(() => RequireViewModel().OpenSelectedAsync());

    private async void CreateDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("新建文件夹", "文件夹名称") { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await RunAsync(() => RequireViewModel().CreateDirectoryAsync(dialog.Value));
        }
    }

    private async void Rename_OnClick(object sender, RoutedEventArgs e)
    {
        var viewModel = RequireViewModel();
        if (viewModel.SelectedEntry is null)
        {
            ShowInfo("请先选择文件或文件夹。");
            return;
        }

        var dialog = new InputDialog("重命名", "新名称", viewModel.SelectedEntry.Name) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await RunAsync(() => viewModel.RenameSelectedAsync(dialog.Value));
        }
    }

    private async void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        var viewModel = RequireViewModel();
        if (viewModel.SelectedEntry is null)
        {
            ShowInfo("请先选择文件或文件夹。");
            return;
        }

        var result = MessageBox.Show(
            this,
            $"确定删除“{viewModel.SelectedEntry.Name}”吗？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            await RunAsync(viewModel.DeleteSelectedAsync);
        }
    }

    private async void Download_OnClick(object sender, RoutedEventArgs e)
    {
        var viewModel = RequireViewModel();
        var entry = viewModel.SelectedEntry;
        if (entry is null || entry.IsDirectory)
        {
            ShowInfo("请选择一个远程文件。");
            return;
        }

        while (true)
        {
            var dialog = new SaveFileDialog
            {
                FileName = entry.Name,
                Title = "选择下载位置",
                OverwritePrompt = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var overwrite = false;
            if (File.Exists(dialog.FileName))
            {
                var localSize = new FileInfo(dialog.FileName).Length;
                var remoteSize = entry.Size ?? 0;
                var conflict = new ConflictDialog(
                    $"本地文件：{localSize:N0} 字节\n远程文件：{remoteSize:N0} 字节")
                {
                    Owner = this
                };
                conflict.ShowDialog();
                if (conflict.Choice == ConflictChoice.Cancel)
                {
                    return;
                }

                if (conflict.Choice == ConflictChoice.Rename)
                {
                    continue;
                }

                if (conflict.Choice == ConflictChoice.Resume && localSize > remoteSize)
                {
                    ShowInfo("本地文件大于远程文件，不能续传。");
                    continue;
                }

                overwrite = conflict.Choice == ConflictChoice.Overwrite;
            }

            await RunAsync(() => viewModel.EnqueueDownloadAsync(dialog.FileName, overwrite));
            return;
        }
    }

    private async void Upload_OnClick(object sender, RoutedEventArgs e)
    {
        var viewModel = RequireViewModel();
        var dialog = new OpenFileDialog { Title = "选择要上传的文件" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var remoteName = Path.GetFileName(dialog.FileName);
        var overwrite = false;
        while (true)
        {
            long? remoteSize = null;
            try
            {
                remoteSize = await viewModel.GetRemoteSizeAsync(remoteName);
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
                return;
            }

            if (remoteSize is null)
            {
                break;
            }

            var localSize = new FileInfo(dialog.FileName).Length;
            var conflict = new ConflictDialog(
                $"本地文件：{localSize:N0} 字节\n远程文件：{remoteSize:N0} 字节")
            {
                Owner = this
            };
            conflict.ShowDialog();
            if (conflict.Choice == ConflictChoice.Cancel)
            {
                return;
            }

            if (conflict.Choice == ConflictChoice.Rename)
            {
                var renameDialog = new InputDialog("上传重命名", "远程文件名", remoteName) { Owner = this };
                if (renameDialog.ShowDialog() != true)
                {
                    return;
                }

                remoteName = renameDialog.Value;
                continue;
            }

            if (conflict.Choice == ConflictChoice.Resume && remoteSize > localSize)
            {
                ShowInfo("远程文件大于本地文件，不能续传。");
                continue;
            }

            overwrite = conflict.Choice == ConflictChoice.Overwrite;
            break;
        }

        await RunAsync(() => viewModel.EnqueueUploadAsync(dialog.FileName, remoteName, overwrite));
    }

    private void PauseTransfer_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransferTask task)
        {
            RequireViewModel().PauseTransfer(task);
        }
    }

    private async void ResumeTransfer_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransferTask task)
        {
            await RunAsync(() => RequireViewModel().ResumeTransferAsync(task));
        }
    }

    private async void RetryTransfer_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransferTask task)
        {
            await RunAsync(() => RequireViewModel().RetryTransferAsync(task));
        }
    }

    private void CancelTransfer_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransferTask task)
        {
            RequireViewModel().CancelTransfer(task);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private MainViewModel RequireViewModel() =>
        _viewModel ?? throw new InvalidOperationException("客户端仍在初始化。");

    private void ShowInfo(string message) =>
        MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
}

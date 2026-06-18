using System.Collections.ObjectModel;
using System.IO;
using FtpClient.Core.Interfaces;
using FtpClient.Core.Models;
using FtpClient.Infrastructure.Ftp;
using FtpClient.Infrastructure.Persistence;
using FtpClient.Infrastructure.Security;
using FtpClient.Infrastructure.Transfers;

namespace FtpClient.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ICredentialProtector _credentialProtector = new DpapiCredentialProtector();
    private readonly IAppLogger? _logger;
    private readonly TransferManager _transferManager;
    private readonly ISiteRepository? _siteRepository;
    private SocketFtpClient? _ftpClient;
    private SiteProfile? _selectedSite;
    private FtpEntry? _selectedEntry;
    private string _host = "127.0.0.1";
    private int _port = 2121;
    private string _username = "ftpuser";
    private string _password = "ftp_password";
    private bool _rememberPassword;
    private string _currentPath = "/";
    private string _statusMessage = "准备就绪";
    private bool _isBusy;
    private bool _isConnected;
    private bool _isTemporaryMode;

    public MainViewModel(
        ISiteRepository? siteRepository,
        TransferManager transferManager,
        bool temporaryMode,
        IAppLogger? logger = null)
    {
        _siteRepository = siteRepository;
        _transferManager = transferManager;
        _logger = logger;
        IsTemporaryMode = temporaryMode;
        Transfers = transferManager.Tasks;
    }

    public ObservableCollection<SiteProfile> Sites { get; } = [];
    public ObservableCollection<FtpEntry> RemoteEntries { get; } = [];
    public ReadOnlyObservableCollection<TransferTask> Transfers { get; }

    public SiteProfile? SelectedSite
    {
        get => _selectedSite;
        set
        {
            if (!SetProperty(ref _selectedSite, value) || value is null)
            {
                return;
            }

            Host = value.Host;
            Port = value.Port;
            Username = value.Username;
            RememberPassword = value.RememberPassword;
            Password = value.RememberPassword && !string.IsNullOrEmpty(value.ProtectedPassword)
                ? TryUnprotect(value.ProtectedPassword)
                : string.Empty;
        }
    }

    public FtpEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public string Host { get => _host; set => SetProperty(ref _host, value); }
    public int Port { get => _port; set => SetProperty(ref _port, value); }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public bool RememberPassword { get => _rememberPassword; set => SetProperty(ref _rememberPassword, value); }
    public string CurrentPath { get => _currentPath; private set => SetProperty(ref _currentPath, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsConnected { get => _isConnected; private set => SetProperty(ref _isConnected, value); }
    public bool IsTemporaryMode { get => _isTemporaryMode; private set => SetProperty(ref _isTemporaryMode, value); }

    public FtpConnectionOptions CurrentConnection =>
        FtpConnectionOptions.Create(Host.Trim(), Port, Username.Trim(), Password);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_siteRepository is not null)
        {
            try
            {
                foreach (var site in await _siteRepository.GetAllAsync(cancellationToken))
                {
                    Sites.Add(site);
                }
            }
            catch
            {
                IsTemporaryMode = true;
            }
        }

        if (!IsTemporaryMode)
        {
            try
            {
                await _transferManager.RestoreAsync(cancellationToken);
            }
            catch
            {
                IsTemporaryMode = true;
            }
        }

        StatusMessage = IsTemporaryMode ? "数据库不可用，当前为临时模式" : "准备就绪";
    }

    public async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(Host) || Port is <= 0 or > 65535 || string.IsNullOrWhiteSpace(Username))
        {
            throw new InvalidOperationException("请填写有效的主机、端口和用户名。");
        }

        IsBusy = true;
        StatusMessage = "正在连接…";
        try
        {
            if (_ftpClient is not null)
            {
                await _ftpClient.DisposeAsync();
            }

            _ftpClient = new SocketFtpClient(logger: _logger);
            await _ftpClient.ConnectAsync(CurrentConnection);
            IsConnected = true;
            await RefreshAsync();
            await SaveSiteAsync();
            StatusMessage = $"已连接到 {Host}:{Port}";
        }
        catch
        {
            IsConnected = false;
            StatusMessage = "连接失败";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_ftpClient is not null)
        {
            await _ftpClient.DisposeAsync();
            _ftpClient = null;
        }

        IsConnected = false;
        RemoteEntries.Clear();
        CurrentPath = "/";
        StatusMessage = "已断开连接";
    }

    public async Task RefreshAsync()
    {
        EnsureConnected();
        IsBusy = true;
        try
        {
            CurrentPath = await _ftpClient!.GetWorkingDirectoryAsync();
            var entries = await _ftpClient.ListAsync();
            RemoteEntries.Clear();
            foreach (var entry in entries.OrderByDescending(item => item.IsDirectory).ThenBy(item => item.Name))
            {
                RemoteEntries.Add(entry);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenSelectedAsync()
    {
        if (SelectedEntry?.IsDirectory != true)
        {
            return;
        }

        await _ftpClient!.ChangeDirectoryAsync(SelectedEntry.FullPath);
        await RefreshAsync();
    }

    public async Task GoParentAsync()
    {
        EnsureConnected();
        await _ftpClient!.ChangeDirectoryAsync("..");
        await RefreshAsync();
    }

    public async Task CreateDirectoryAsync(string name)
    {
        EnsureConnected();
        await _ftpClient!.CreateDirectoryAsync(FtpListParser.CombineRemotePath(CurrentPath, name));
        await RefreshAsync();
    }

    public async Task RenameSelectedAsync(string newName)
    {
        EnsureConnected();
        if (SelectedEntry is null)
        {
            return;
        }

        await _ftpClient!.RenameAsync(
            SelectedEntry.FullPath,
            FtpListParser.CombineRemotePath(CurrentPath, newName));
        await RefreshAsync();
    }

    public async Task DeleteSelectedAsync()
    {
        EnsureConnected();
        if (SelectedEntry is null)
        {
            return;
        }

        if (SelectedEntry.IsDirectory)
        {
            await _ftpClient!.DeleteDirectoryAsync(SelectedEntry.FullPath);
        }
        else
        {
            await _ftpClient!.DeleteFileAsync(SelectedEntry.FullPath);
        }

        await RefreshAsync();
    }

    public async Task EnqueueDownloadAsync(string localPath, bool overwrite)
    {
        EnsureConnected();
        if (SelectedEntry is null || SelectedEntry.IsDirectory)
        {
            throw new InvalidOperationException("请选择一个远程文件。");
        }

        if (overwrite && File.Exists(localPath))
        {
            File.Delete(localPath);
        }

        await _transferManager.EnqueueAsync(new TransferTask
        {
            SiteId = SelectedSite?.Id,
            Direction = TransferDirection.Download,
            LocalPath = localPath,
            RemotePath = SelectedEntry.FullPath,
            TotalBytes = SelectedEntry.Size ?? 0,
            TransferredBytes = File.Exists(localPath) ? new FileInfo(localPath).Length : 0
        }, CurrentConnection);
    }

    public async Task EnqueueUploadAsync(string localPath, string remoteName, bool overwrite)
    {
        EnsureConnected();
        var remotePath = FtpListParser.CombineRemotePath(CurrentPath, remoteName);
        if (overwrite)
        {
            var existingSize = await _ftpClient!.GetFileSizeAsync(remotePath);
            if (existingSize is not null)
            {
                await _ftpClient.DeleteFileAsync(remotePath);
            }
        }

        await _transferManager.EnqueueAsync(new TransferTask
        {
            SiteId = SelectedSite?.Id,
            Direction = TransferDirection.Upload,
            LocalPath = localPath,
            RemotePath = remotePath,
            TotalBytes = new FileInfo(localPath).Length
        }, CurrentConnection);
    }

    public Task<long?> GetRemoteSizeAsync(string remoteName)
    {
        EnsureConnected();
        return _ftpClient!.GetFileSizeAsync(FtpListParser.CombineRemotePath(CurrentPath, remoteName));
    }

    public void PauseTransfer(TransferTask task) => _transferManager.Pause(task.Id);
    public void CancelTransfer(TransferTask task) => _transferManager.Cancel(task.Id);
    public Task ResumeTransferAsync(TransferTask task) => _transferManager.ResumeAsync(task.Id, CurrentConnection);
    public Task RetryTransferAsync(TransferTask task) => _transferManager.RetryAsync(task.Id, CurrentConnection);

    private async Task SaveSiteAsync()
    {
        if (_siteRepository is null || IsTemporaryMode)
        {
            return;
        }

        try
        {
            var site = new SiteProfile
            {
                Name = $"{Username}@{Host}",
                Host = Host.Trim(),
                Port = Port,
                Username = Username.Trim(),
                RememberPassword = RememberPassword,
                ProtectedPassword = RememberPassword ? _credentialProtector.Protect(Password) : null,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            site.Id = await _siteRepository.UpsertAsync(site);
            var existing = Sites.FirstOrDefault(item =>
                item.Host == site.Host && item.Port == site.Port && item.Username == site.Username);
            if (existing is not null)
            {
                Sites.Remove(existing);
            }

            Sites.Insert(0, site);
            _selectedSite = site;
            OnPropertyChanged(nameof(SelectedSite));
        }
        catch
        {
            IsTemporaryMode = true;
            StatusMessage = "数据库写入失败，已切换到临时模式";
        }
    }

    private string TryUnprotect(string value)
    {
        try
        {
            return _credentialProtector.Unprotect(value);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void EnsureConnected()
    {
        if (_ftpClient is null || !IsConnected)
        {
            throw new InvalidOperationException("请先连接 FTP 服务器。");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ftpClient is not null)
        {
            await _ftpClient.DisposeAsync();
        }

        await _transferManager.DisposeAsync();
    }
}

using FtpClient.Core.Models;

namespace FtpClient.Core.Interfaces;

public interface IFtpClient : IAsyncDisposable
{
    bool IsConnected { get; }
    FtpConnectionOptions? ConnectionOptions { get; }

    Task ConnectAsync(FtpConnectionOptions options, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<string> GetWorkingDirectoryAsync(CancellationToken cancellationToken = default);
    Task ChangeDirectoryAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FtpEntry>> ListAsync(string? path = null, CancellationToken cancellationToken = default);
    Task<long?> GetFileSizeAsync(string path, CancellationToken cancellationToken = default);
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);
    Task RenameAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    Task DownloadAsync(string remotePath, Stream destination, long offset, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
    Task UploadAsync(string remotePath, Stream source, long offset, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}

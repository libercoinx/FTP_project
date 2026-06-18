using System.Collections.ObjectModel;
using FtpClient.Core.Models;

namespace FtpClient.Core.Interfaces;

public interface ITransferManager : IAsyncDisposable
{
    ReadOnlyObservableCollection<TransferTask> Tasks { get; }
    Task EnqueueAsync(TransferTask task, FtpConnectionOptions connection, CancellationToken cancellationToken = default);
    void Pause(Guid taskId);
    Task ResumeAsync(Guid taskId, FtpConnectionOptions connection, CancellationToken cancellationToken = default);
    void Cancel(Guid taskId);
    Task RetryAsync(Guid taskId, FtpConnectionOptions connection, CancellationToken cancellationToken = default);
}

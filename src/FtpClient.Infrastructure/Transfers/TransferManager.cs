using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Channels;
using FtpClient.Core.Interfaces;
using FtpClient.Core.Models;
using FtpClient.Infrastructure.Ftp;

namespace FtpClient.Infrastructure.Transfers;

public sealed class TransferManager : ITransferManager
{
    private readonly ObservableCollection<TransferTask> _tasks = [];
    private readonly Channel<QueuedTransfer> _queue = Channel.CreateUnbounded<QueuedTransfer>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();
    private readonly ITransferRepository? _repository;
    private readonly IAppLogger? _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;

    public TransferManager(
        ITransferRepository? repository = null,
        int concurrency = 2,
        IAppLogger? logger = null)
    {
        _repository = repository;
        _logger = logger;
        Tasks = new ReadOnlyObservableCollection<TransferTask>(_tasks);
        _workers = Enumerable.Range(0, Math.Max(1, concurrency))
            .Select(_ => Task.Run(() => WorkerAsync(_shutdown.Token)))
            .ToArray();
    }

    public ReadOnlyObservableCollection<TransferTask> Tasks { get; }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (_repository is null)
        {
            return;
        }

        foreach (var task in await _repository.GetRecoverableAsync(cancellationToken))
        {
            _tasks.Add(task);
        }
    }

    public async Task EnqueueAsync(
        TransferTask task,
        FtpConnectionOptions connection,
        CancellationToken cancellationToken = default)
    {
        task.Status = TransferStatus.Queued;
        task.ErrorMessage = null;
        if (!_tasks.Contains(task))
        {
            _tasks.Add(task);
        }

        await SafePersistAsync(task, cancellationToken);
        await _queue.Writer.WriteAsync(new QueuedTransfer(task, connection), cancellationToken);
    }

    public void Pause(Guid taskId)
    {
        var task = Find(taskId);
        if (task is null || task.Status is TransferStatus.Completed or TransferStatus.Cancelled)
        {
            return;
        }

        task.Status = TransferStatus.Paused;
        if (_cancellations.TryGetValue(taskId, out var source))
        {
            source.Cancel();
        }

        _ = SafePersistAsync(task, CancellationToken.None);
    }

    public Task ResumeAsync(
        Guid taskId,
        FtpConnectionOptions connection,
        CancellationToken cancellationToken = default)
    {
        var task = Find(taskId) ?? throw new InvalidOperationException("找不到传输任务。");
        return EnqueueAsync(task, connection, cancellationToken);
    }

    public void Cancel(Guid taskId)
    {
        var task = Find(taskId);
        if (task is null || task.Status == TransferStatus.Completed)
        {
            return;
        }

        task.Status = TransferStatus.Cancelled;
        if (_cancellations.TryGetValue(taskId, out var source))
        {
            source.Cancel();
        }

        _ = SafePersistAsync(task, CancellationToken.None);
    }

    public Task RetryAsync(
        Guid taskId,
        FtpConnectionOptions connection,
        CancellationToken cancellationToken = default)
    {
        var task = Find(taskId) ?? throw new InvalidOperationException("找不到传输任务。");
        task.ErrorMessage = null;
        return EnqueueAsync(task, connection, cancellationToken);
    }

    private async Task WorkerAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(cancellationToken))
        {
            if (item.Task.Status != TransferStatus.Queued)
            {
                continue;
            }

            using var taskSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!_cancellations.TryAdd(item.Task.Id, taskSource))
            {
                continue;
            }

            try
            {
                await ExecuteAsync(item.Task, item.Connection, taskSource.Token);
            }
            catch (OperationCanceledException)
            {
                if (item.Task.Status == TransferStatus.Running)
                {
                    item.Task.Status = TransferStatus.Paused;
                }
            }
            catch (Exception exception)
            {
                item.Task.Status = TransferStatus.Failed;
                item.Task.ErrorMessage = exception.Message;
                if (_logger is not null)
                {
                    await _logger.ErrorAsync(
                        LogCategory.Transfer,
                        $"任务 {item.Task.Id} 失败：{item.Task.RemotePath}",
                        exception,
                        CancellationToken.None);
                }
            }
            finally
            {
                item.Task.BytesPerSecond = 0;
                item.Task.UpdatedAt = DateTimeOffset.UtcNow;
                _cancellations.TryRemove(item.Task.Id, out _);
                await SafePersistAsync(item.Task, CancellationToken.None);
            }
        }
    }

    private async Task ExecuteAsync(
        TransferTask task,
        FtpConnectionOptions connection,
        CancellationToken cancellationToken)
    {
        task.Status = TransferStatus.Running;
        task.ErrorMessage = null;
        await SafePersistAsync(task, cancellationToken);

        await using var client = new SocketFtpClient(logger: _logger);
        if (_logger is not null)
        {
            await _logger.InfoAsync(
                LogCategory.Transfer,
                $"开始任务 {task.Id}：{task.Direction} {task.RemotePath}",
                cancellationToken);
        }
        await client.ConnectAsync(connection, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var lastBytes = task.TransferredBytes;
        var lastTimestamp = stopwatch.Elapsed;
        var progress = new Progress<long>(bytes =>
        {
            task.TransferredBytes = bytes;
            var now = stopwatch.Elapsed;
            var elapsed = (now - lastTimestamp).TotalSeconds;
            if (elapsed >= 0.5)
            {
                task.BytesPerSecond = Math.Max(0, (bytes - lastBytes) / elapsed);
                lastBytes = bytes;
                lastTimestamp = now;
                task.UpdatedAt = DateTimeOffset.UtcNow;
            }
        });

        if (task.Direction == TransferDirection.Download)
        {
            var remoteSize = await client.GetFileSizeAsync(task.RemotePath, cancellationToken)
                ?? throw new InvalidOperationException("服务器未返回远程文件大小。");
            task.TotalBytes = remoteSize;

            var existingLength = File.Exists(task.LocalPath) ? new FileInfo(task.LocalPath).Length : 0;
            if (existingLength > remoteSize)
            {
                throw new InvalidOperationException("本地文件大于远程文件，无法自动续传。");
            }

            task.TransferredBytes = existingLength;
            var mode = existingLength == 0 ? FileMode.Create : FileMode.OpenOrCreate;
            await using var output = new FileStream(task.LocalPath, mode, FileAccess.Write, FileShare.Read, 64 * 1024, true);
            output.Position = existingLength;
            await client.DownloadAsync(task.RemotePath, output, existingLength, progress, cancellationToken);
        }
        else
        {
            if (!File.Exists(task.LocalPath))
            {
                throw new FileNotFoundException("待上传的本地文件不存在。", task.LocalPath);
            }

            var localSize = new FileInfo(task.LocalPath).Length;
            var remoteSize = await client.GetFileSizeAsync(task.RemotePath, cancellationToken) ?? 0;
            if (remoteSize > localSize)
            {
                throw new InvalidOperationException("远程文件大于本地文件，无法自动续传。");
            }

            task.TotalBytes = localSize;
            task.TransferredBytes = remoteSize;
            await using var input = new FileStream(task.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            input.Position = remoteSize;
            await client.UploadAsync(task.RemotePath, input, remoteSize, progress, cancellationToken);
        }

        task.TransferredBytes = task.TotalBytes;
        task.Status = TransferStatus.Completed;
        if (_logger is not null)
        {
            await _logger.InfoAsync(
                LogCategory.Transfer,
                $"完成任务 {task.Id}：{task.TotalBytes} 字节",
                cancellationToken);
        }
    }

    private TransferTask? Find(Guid id) => _tasks.FirstOrDefault(task => task.Id == id);

    private async Task SafePersistAsync(TransferTask task, CancellationToken cancellationToken)
    {
        if (_repository is null)
        {
            return;
        }

        try
        {
            await _repository.UpsertAsync(task, cancellationToken);
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _queue.Writer.TryComplete();
        foreach (var source in _cancellations.Values)
        {
            source.Cancel();
        }

        try
        {
            await Task.WhenAll(_workers);
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
    }

    private sealed record QueuedTransfer(TransferTask Task, FtpConnectionOptions Connection);
}

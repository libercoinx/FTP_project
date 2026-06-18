using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FtpClient.Core.Models;

public enum TransferDirection
{
    Download,
    Upload
}

public enum TransferStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public sealed class TransferTask : INotifyPropertyChanged
{
    private long _transferredBytes;
    private TransferStatus _status = TransferStatus.Queued;
    private string? _errorMessage;
    private double _bytesPerSecond;

    public Guid Id { get; init; } = Guid.NewGuid();
    public long? SiteId { get; init; }
    public TransferDirection Direction { get; init; }
    public string LocalPath { get; init; } = string.Empty;
    public string RemotePath { get; init; } = string.Empty;
    public long TotalBytes { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public long TransferredBytes
    {
        get => _transferredBytes;
        set
        {
            if (SetField(ref _transferredBytes, value))
            {
                OnPropertyChanged(nameof(Progress));
            }
        }
    }

    public TransferStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public double BytesPerSecond
    {
        get => _bytesPerSecond;
        set => SetField(ref _bytesPerSecond, value);
    }

    public double Progress => TotalBytes <= 0 ? 0 : Math.Clamp(TransferredBytes * 100d / TotalBytes, 0, 100);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

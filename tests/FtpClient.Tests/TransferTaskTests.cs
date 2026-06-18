using FtpClient.Core.Models;

namespace FtpClient.Tests;

public sealed class TransferTaskTests
{
    [Fact]
    public void Progress_IsCalculatedAndClamped()
    {
        var task = new TransferTask { TotalBytes = 200 };

        task.TransferredBytes = 50;
        Assert.Equal(25, task.Progress);

        task.TransferredBytes = 300;
        Assert.Equal(100, task.Progress);
    }

    [Fact]
    public void RecoverableTaskCanBeResetToPaused()
    {
        var task = new TransferTask
        {
            Status = TransferStatus.Running,
            TransferredBytes = 1024,
            TotalBytes = 4096
        };

        task.Status = TransferStatus.Paused;

        Assert.Equal(TransferStatus.Paused, task.Status);
        Assert.Equal(25, task.Progress);
    }
}

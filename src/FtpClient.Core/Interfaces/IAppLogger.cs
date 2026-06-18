namespace FtpClient.Core.Interfaces;

public enum LogCategory
{
    Connection,
    Protocol,
    Transfer,
    Database
}

public interface IAppLogger
{
    Task InfoAsync(LogCategory category, string message, CancellationToken cancellationToken = default);
    Task ErrorAsync(LogCategory category, string message, Exception? exception = null, CancellationToken cancellationToken = default);
}

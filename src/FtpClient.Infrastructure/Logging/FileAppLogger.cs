using System.Text;
using System.Text.RegularExpressions;
using FtpClient.Core.Interfaces;

namespace FtpClient.Infrastructure.Logging;

public sealed partial class FileAppLogger : IAppLogger, IDisposable
{
    private readonly string _logPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileAppLogger(string? directory = null)
    {
        var logDirectory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SocketFtpClient",
            "logs");
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, $"ftp-client-{DateTime.Now:yyyyMMdd}.log");
    }

    public Task InfoAsync(
        LogCategory category,
        string message,
        CancellationToken cancellationToken = default) =>
        WriteAsync("INFO", category, message, null, cancellationToken);

    public Task ErrorAsync(
        LogCategory category,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync("ERROR", category, message, exception, cancellationToken);

    private async Task WriteAsync(
        string level,
        LogCategory category,
        string message,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        var safeMessage = Redact(message);
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" [").Append(level).Append("] [").Append(category).Append("] ")
            .Append(safeMessage);
        if (exception is not null)
        {
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(Redact(exception.Message));
        }

        line.AppendLine();
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_logPath, line.ToString(), Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public static string Redact(string value) =>
        PasswordCommandRegex().Replace(
            ConnectionStringPasswordRegex().Replace(value, "${key}=***"),
            "PASS ***");

    public void Dispose() => _writeLock.Dispose();

    [GeneratedRegex(@"(?<key>Password|Pwd)\s*=\s*[^;\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPasswordRegex();

    [GeneratedRegex(@"PASS\s+.*", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordCommandRegex();
}

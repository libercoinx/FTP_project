using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using FtpClient.Core.Exceptions;
using FtpClient.Core.Interfaces;
using FtpClient.Core.Models;

namespace FtpClient.Infrastructure.Ftp;

public sealed partial class SocketFtpClient : IFtpClient
{
    private readonly IFtpDataConnectionFactory _dataConnectionFactory;
    private readonly IAppLogger? _logger;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private TcpClient? _controlClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public SocketFtpClient(
        IFtpDataConnectionFactory? dataConnectionFactory = null,
        IAppLogger? logger = null)
    {
        _dataConnectionFactory = dataConnectionFactory ?? new PassiveDataConnectionFactory();
        _logger = logger;
    }

    public bool IsConnected => _controlClient?.Connected == true;
    public FtpConnectionOptions? ConnectionOptions { get; private set; }

    public async Task ConnectAsync(FtpConnectionOptions options, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            await DisconnectAsync(cancellationToken);
        }

        var client = new TcpClient();
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(options.Timeout);
            await client.ConnectAsync(options.Host, options.Port, timeoutSource.Token);

            var stream = client.GetStream();
            _controlClient = client;
            _reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
            _writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };
            ConnectionOptions = options;
            await LogInfoAsync(LogCategory.Connection, $"已连接 {options.Host}:{options.Port}", cancellationToken);

            EnsureCompletion(await ReadReplyAsync(cancellationToken), "连接");
            var userReply = await SendCommandAsync($"USER {options.Username}", cancellationToken);
            if (userReply.Code == 331)
            {
                EnsureCompletion(await SendCommandAsync($"PASS {options.Password}", cancellationToken), "登录");
            }
            else
            {
                EnsureCompletion(userReply, "登录");
            }

            await SendCommandAsync("OPTS UTF8 ON", cancellationToken);
            EnsureCompletion(await SendCommandAsync("TYPE I", cancellationToken), "设置二进制传输模式");
        }
        catch
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync(
                    LogCategory.Connection,
                    $"连接 {options.Host}:{options.Port} 失败",
                    cancellationToken: CancellationToken.None);
            }
            client.Dispose();
            ResetConnection();
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_writer is not null)
        {
            try
            {
                await SendCommandAsync("QUIT", cancellationToken);
            }
            catch
            {
            }
        }

        _reader?.Dispose();
        _writer?.Dispose();
        _controlClient?.Dispose();
        ResetConnection();
        await LogInfoAsync(LogCategory.Connection, "连接已关闭", CancellationToken.None);
    }

    public async Task<string> GetWorkingDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var reply = await SendCommandAsync("PWD", cancellationToken);
        EnsureCompletion(reply, "读取当前目录");
        var match = QuotedPathRegex().Match(reply.Message);
        return match.Success ? match.Groups["path"].Value.Replace("\"\"", "\"") : "/";
    }

    public async Task ChangeDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        EnsureCompletion(await SendCommandAsync($"CWD {path}", cancellationToken), "切换目录");

    public async Task<IReadOnlyList<FtpEntry>> ListAsync(
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        var currentPath = path ?? await GetWorkingDirectoryAsync(cancellationToken);
        try
        {
            var content = await ReceiveTextDataAsync(
                string.IsNullOrWhiteSpace(path) ? "MLSD" : $"MLSD {path}",
                cancellationToken);
            return FtpListParser.ParseMlsd(content, currentPath);
        }
        catch (FtpException)
        {
            var content = await ReceiveTextDataAsync(
                string.IsNullOrWhiteSpace(path) ? "LIST" : $"LIST {path}",
                cancellationToken);
            return FtpListParser.ParseUnixList(content, currentPath);
        }
    }

    public async Task<long?> GetFileSizeAsync(string path, CancellationToken cancellationToken = default)
    {
        var reply = await SendCommandAsync($"SIZE {path}", cancellationToken);
        return reply.IsPositiveCompletion && long.TryParse(reply.Message.Trim(), out var size) ? size : null;
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        EnsureCompletion(await SendCommandAsync($"MKD {path}", cancellationToken), "新建目录");

    public async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        EnsureCompletion(await SendCommandAsync($"RMD {path}", cancellationToken), "删除目录");

    public async Task DeleteFileAsync(string path, CancellationToken cancellationToken = default) =>
        EnsureCompletion(await SendCommandAsync($"DELE {path}", cancellationToken), "删除文件");

    public async Task RenameAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var firstReply = await SendCommandAsync($"RNFR {sourcePath}", cancellationToken);
        if (!firstReply.IsPositiveIntermediate)
        {
            throw new FtpException($"重命名失败：{firstReply.Message}", firstReply.Code);
        }

        EnsureCompletion(await SendCommandAsync($"RNTO {destinationPath}", cancellationToken), "重命名");
    }

    public Task DownloadAsync(
        string remotePath,
        Stream destination,
        long offset,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        TransferAsync($"RETR {remotePath}", destination, offset, false, progress, cancellationToken);

    public Task UploadAsync(
        string remotePath,
        Stream source,
        long offset,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        TransferAsync($"STOR {remotePath}", source, offset, true, progress, cancellationToken);

    internal async Task<FtpReply> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            await LogInfoAsync(LogCategory.Protocol, SanitizeCommand(command), cancellationToken);
            await _writer!.WriteLineAsync(command.AsMemory(), cancellationToken);
            return await ReadReplyAsync(cancellationToken);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task<string> ReceiveTextDataAsync(string command, CancellationToken cancellationToken)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            using var dataClient = await _dataConnectionFactory.OpenAsync(
                SendCommandWithoutLockAsync,
                ConnectionOptions!.Host,
                ConnectionOptions.Timeout,
                cancellationToken);

            var preliminary = await SendCommandWithoutLockAsync(command, cancellationToken);
            EnsurePreliminary(preliminary, command);

            using var reader = new StreamReader(dataClient.GetStream(), Encoding.UTF8, true, 4096, false);
            var content = await reader.ReadToEndAsync(cancellationToken);
            EnsureCompletion(await ReadReplyAsync(cancellationToken), command);
            return content;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task TransferAsync(
        string command,
        Stream stream,
        long offset,
        bool upload,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            using var dataClient = await _dataConnectionFactory.OpenAsync(
                SendCommandWithoutLockAsync,
                ConnectionOptions!.Host,
                ConnectionOptions.Timeout,
                cancellationToken);

            if (offset > 0)
            {
                var restReply = await SendCommandWithoutLockAsync($"REST {offset}", cancellationToken);
                if (!restReply.IsPositiveIntermediate)
                {
                    throw new FtpException($"服务器不支持从偏移 {offset} 续传：{restReply.Message}", restReply.Code);
                }
            }

            EnsurePreliminary(await SendCommandWithoutLockAsync(command, cancellationToken), command);
            var dataStream = dataClient.GetStream();
            var buffer = new byte[64 * 1024];
            long transferred = offset;
            int read;

            if (upload)
            {
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await dataStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    transferred += read;
                    progress?.Report(transferred);
                }

                dataClient.Client.Shutdown(SocketShutdown.Send);
            }
            else
            {
                while ((read = await dataStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    transferred += read;
                    progress?.Report(transferred);
                }
            }

            EnsureCompletion(await ReadReplyAsync(cancellationToken), command);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task<FtpReply> SendCommandWithoutLockAsync(string command, CancellationToken cancellationToken)
    {
        EnsureConnected();
        await LogInfoAsync(LogCategory.Protocol, SanitizeCommand(command), cancellationToken);
        await _writer!.WriteLineAsync(command.AsMemory(), cancellationToken);
        return await ReadReplyAsync(cancellationToken);
    }

    private async Task<FtpReply> ReadReplyAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();
        var firstLine = await _reader!.ReadLineAsync(cancellationToken)
            ?? throw new FtpException("FTP 服务器已断开连接。");
        if (firstLine.Length < 3 || !int.TryParse(firstLine[..3], out var code))
        {
            throw new FtpException($"无效的 FTP 响应：{firstLine}");
        }

        var lines = new List<string> { firstLine.Length > 4 ? firstLine[4..] : string.Empty };
        if (firstLine.Length > 3 && firstLine[3] == '-')
        {
            var terminator = $"{code} ";
            while (true)
            {
                var line = await _reader.ReadLineAsync(cancellationToken)
                    ?? throw new FtpException("读取多行响应时连接中断。");
                lines.Add(line.StartsWith(terminator, StringComparison.Ordinal) ? line[4..] : line);
                if (line.StartsWith(terminator, StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        var reply = new FtpReply(code, string.Join(Environment.NewLine, lines));
        await LogInfoAsync(LogCategory.Protocol, $"响应 {reply.Code} {reply.Message}", cancellationToken);
        return reply;
    }

    private static void EnsureCompletion(FtpReply reply, string action)
    {
        if (!reply.IsPositiveCompletion)
        {
            throw new FtpException($"{action}失败：{reply.Message}", reply.Code);
        }
    }

    private static void EnsurePreliminary(FtpReply reply, string action)
    {
        if (!reply.IsPositivePreliminary)
        {
            throw new FtpException($"{action} 未开始：{reply.Message}", reply.Code);
        }
    }

    private void EnsureConnected()
    {
        if (_controlClient is null || _reader is null || _writer is null || ConnectionOptions is null)
        {
            throw new FtpException("尚未连接 FTP 服务器。");
        }
    }

    private void ResetConnection()
    {
        _controlClient = null;
        _reader = null;
        _writer = null;
        ConnectionOptions = null;
    }

    private Task LogInfoAsync(LogCategory category, string message, CancellationToken cancellationToken) =>
        _logger?.InfoAsync(category, message, cancellationToken) ?? Task.CompletedTask;

    private static string SanitizeCommand(string command) =>
        command.StartsWith("PASS ", StringComparison.OrdinalIgnoreCase) ? "PASS ***" : $"命令 {command}";

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _commandLock.Dispose();
    }

    [GeneratedRegex("\"(?<path>(?:[^\"]|\"\")*)\"")]
    private static partial Regex QuotedPathRegex();
}

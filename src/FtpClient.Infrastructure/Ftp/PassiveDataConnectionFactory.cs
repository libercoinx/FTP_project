using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using FtpClient.Core.Exceptions;
using FtpClient.Core.Interfaces;
using FtpClient.Core.Models;

namespace FtpClient.Infrastructure.Ftp;

public sealed partial class PassiveDataConnectionFactory : IFtpDataConnectionFactory
{
    public async Task<TcpClient> OpenAsync(
        Func<string, CancellationToken, Task<FtpReply>> commandSender,
        string controlHost,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var epsvReply = await commandSender("EPSV", cancellationToken);
        if (epsvReply.IsPositiveCompletion)
        {
            var match = EpsvRegex().Match(epsvReply.Message);
            if (match.Success && int.TryParse(match.Groups["port"].Value, out var epsvPort))
            {
                return await ConnectAsync(controlHost, epsvPort, timeout, cancellationToken);
            }
        }

        var pasvReply = await commandSender("PASV", cancellationToken);
        if (!pasvReply.IsPositiveCompletion)
        {
            throw new FtpException($"服务器拒绝被动模式：{pasvReply.Message}", pasvReply.Code);
        }

        var pasvMatch = PasvRegex().Match(pasvReply.Message);
        if (!pasvMatch.Success)
        {
            throw new FtpException($"无法解析 PASV 响应：{pasvReply.Message}", pasvReply.Code);
        }

        var numbers = Enumerable.Range(1, 6)
            .Select(index => int.Parse(pasvMatch.Groups[$"n{index}"].Value))
            .ToArray();
        var advertisedHost = string.Join('.', numbers.Take(4));
        var host = IPAddress.TryParse(advertisedHost, out var address) && IsPrivateOrLoopback(address)
            ? controlHost
            : advertisedHost;
        var port = numbers[4] * 256 + numbers[5];

        return await ConnectAsync(host, port, timeout, cancellationToken);
    }

    private static async Task<TcpClient> ConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await client.ConnectAsync(host, port, timeoutSource.Token);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            && (bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31);
    }

    [GeneratedRegex(@"\(\|\|\|(?<port>\d+)\|\)")]
    private static partial Regex EpsvRegex();

    [GeneratedRegex(@"\((?<n1>\d+),(?<n2>\d+),(?<n3>\d+),(?<n4>\d+),(?<n5>\d+),(?<n6>\d+)\)")]
    private static partial Regex PasvRegex();
}

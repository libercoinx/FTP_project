using System.Net.Sockets;

namespace FtpClient.Core.Interfaces;

public interface IFtpDataConnectionFactory
{
    Task<TcpClient> OpenAsync(
        Func<string, CancellationToken, Task<Models.FtpReply>> commandSender,
        string controlHost,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

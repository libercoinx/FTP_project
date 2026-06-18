namespace FtpClient.Core.Models;

public sealed record FtpConnectionOptions(
    string Host,
    int Port,
    string Username,
    string Password,
    TimeSpan Timeout)
{
    public static FtpConnectionOptions Create(
        string host,
        int port,
        string username,
        string password) =>
        new(host, port, username, password, TimeSpan.FromSeconds(15));
}

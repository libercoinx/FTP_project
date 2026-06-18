namespace FtpClient.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public string ConnectionString { get; init; } =
        "Server=127.0.0.1;Port=3307;Database=ftp_client;User ID=ftp_app;Password=ftp_app_password;SslMode=None;AllowPublicKeyRetrieval=True;";
}

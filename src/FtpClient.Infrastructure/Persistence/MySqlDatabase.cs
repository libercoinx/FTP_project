using MySqlConnector;

namespace FtpClient.Infrastructure.Persistence;

public sealed class MySqlDatabase
{
    private readonly string _connectionString;

    public MySqlDatabase(DatabaseOptions options)
    {
        _connectionString = options.ConnectionString;
    }

    public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS sites (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                host VARCHAR(255) NOT NULL,
                port INT NOT NULL,
                username VARCHAR(255) NOT NULL,
                protected_password TEXT NULL,
                remember_password BOOLEAN NOT NULL DEFAULT FALSE,
                updated_at DATETIME(6) NOT NULL,
                UNIQUE KEY uq_site_endpoint (host, port, username)
            );

            CREATE TABLE IF NOT EXISTS transfer_tasks (
                id CHAR(36) NOT NULL PRIMARY KEY,
                site_id BIGINT NULL,
                direction VARCHAR(16) NOT NULL,
                local_path TEXT NOT NULL,
                remote_path TEXT NOT NULL,
                total_bytes BIGINT NOT NULL,
                transferred_bytes BIGINT NOT NULL,
                status VARCHAR(16) NOT NULL,
                error_message TEXT NULL,
                created_at DATETIME(6) NOT NULL,
                updated_at DATETIME(6) NOT NULL,
                INDEX ix_transfer_status (status)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

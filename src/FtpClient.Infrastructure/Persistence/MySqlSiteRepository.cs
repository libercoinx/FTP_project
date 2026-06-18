using FtpClient.Core.Interfaces;
using FtpClient.Core.Models;
using MySqlConnector;

namespace FtpClient.Infrastructure.Persistence;

public sealed class MySqlSiteRepository : ISiteRepository
{
    private readonly MySqlDatabase _database;

    public MySqlSiteRepository(MySqlDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<SiteProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var sites = new List<SiteProfile>();
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, host, port, username, protected_password, remember_password, updated_at
            FROM sites
            ORDER BY updated_at DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sites.Add(new SiteProfile
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Host = reader.GetString(2),
                Port = reader.GetInt32(3),
                Username = reader.GetString(4),
                ProtectedPassword = reader.IsDBNull(5) ? null : reader.GetString(5),
                RememberPassword = reader.GetBoolean(6),
                UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc))
            });
        }

        return sites;
    }

    public async Task<long> UpsertAsync(SiteProfile site, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sites (name, host, port, username, protected_password, remember_password, updated_at)
            VALUES (@name, @host, @port, @username, @password, @remember, @updated)
            ON DUPLICATE KEY UPDATE
                id = LAST_INSERT_ID(id),
                name = VALUES(name),
                protected_password = VALUES(protected_password),
                remember_password = VALUES(remember_password),
                updated_at = VALUES(updated_at);
            """;
        command.Parameters.AddWithValue("@name", site.Name);
        command.Parameters.AddWithValue("@host", site.Host);
        command.Parameters.AddWithValue("@port", site.Port);
        command.Parameters.AddWithValue("@username", site.Username);
        command.Parameters.AddWithValue("@password", (object?)site.ProtectedPassword ?? DBNull.Value);
        command.Parameters.AddWithValue("@remember", site.RememberPassword);
        command.Parameters.AddWithValue("@updated", site.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return command.LastInsertedId;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sites WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

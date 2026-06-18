using FtpClient.Core.Interfaces;
using FtpClient.Core.Models;

namespace FtpClient.Infrastructure.Persistence;

public sealed class MySqlTransferRepository : ITransferRepository
{
    private readonly MySqlDatabase _database;

    public MySqlTransferRepository(MySqlDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<TransferTask>> GetRecoverableAsync(CancellationToken cancellationToken = default)
    {
        var tasks = new List<TransferTask>();
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, site_id, direction, local_path, remote_path, total_bytes,
                   transferred_bytes, error_message, created_at, updated_at
            FROM transfer_tasks
            WHERE status IN ('Queued', 'Running', 'Paused', 'Failed')
            ORDER BY created_at;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(new TransferTask
            {
                Id = reader.GetGuid(0),
                SiteId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                Direction = Enum.Parse<TransferDirection>(reader.GetString(2)),
                LocalPath = reader.GetString(3),
                RemotePath = reader.GetString(4),
                TotalBytes = reader.GetInt64(5),
                TransferredBytes = reader.GetInt64(6),
                Status = TransferStatus.Paused,
                ErrorMessage = reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc)),
                UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc))
            });
        }

        return tasks;
    }

    public async Task UpsertAsync(TransferTask task, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO transfer_tasks (
                id, site_id, direction, local_path, remote_path, total_bytes,
                transferred_bytes, status, error_message, created_at, updated_at)
            VALUES (
                @id, @siteId, @direction, @localPath, @remotePath, @totalBytes,
                @transferredBytes, @status, @errorMessage, @createdAt, @updatedAt)
            ON DUPLICATE KEY UPDATE
                site_id = VALUES(site_id),
                total_bytes = VALUES(total_bytes),
                transferred_bytes = VALUES(transferred_bytes),
                status = VALUES(status),
                error_message = VALUES(error_message),
                updated_at = VALUES(updated_at);
            """;
        command.Parameters.AddWithValue("@id", task.Id.ToString());
        command.Parameters.AddWithValue("@siteId", (object?)task.SiteId ?? DBNull.Value);
        command.Parameters.AddWithValue("@direction", task.Direction.ToString());
        command.Parameters.AddWithValue("@localPath", task.LocalPath);
        command.Parameters.AddWithValue("@remotePath", task.RemotePath);
        command.Parameters.AddWithValue("@totalBytes", task.TotalBytes);
        command.Parameters.AddWithValue("@transferredBytes", task.TransferredBytes);
        command.Parameters.AddWithValue("@status", task.Status.ToString());
        command.Parameters.AddWithValue("@errorMessage", (object?)task.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", task.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAt", task.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM transfer_tasks WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

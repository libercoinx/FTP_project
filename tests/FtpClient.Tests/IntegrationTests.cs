using FtpClient.Core.Models;
using FtpClient.Infrastructure.Ftp;
using FtpClient.Infrastructure.Persistence;

namespace FtpClient.Tests;

public sealed class IntegrationTests
{
    [Fact]
    public async Task DockerFtp_UploadListResumeDownloadRenameAndDelete()
    {
        if (Environment.GetEnvironmentVariable("RUN_FTP_INTEGRATION") != "1")
        {
            return;
        }

        var options = FtpConnectionOptions.Create("127.0.0.1", 2121, "ftpuser", "ftp_password");
        await using var client = new SocketFtpClient();
        await client.ConnectAsync(options);

        var directory = $"/integration-{Guid.NewGuid():N}";
        var originalPath = $"{directory}/中文 test.bin";
        var renamedPath = $"{directory}/renamed.bin";
        var content = Enumerable.Range(0, 32 * 1024)
            .Select(index => (byte)(index % 251))
            .ToArray();

        try
        {
            await client.CreateDirectoryAsync(directory);
            await using (var source = new MemoryStream(content, writable: false))
            {
                await client.UploadAsync(originalPath, source, 0);
            }

            Assert.Equal(content.Length, await client.GetFileSizeAsync(originalPath));
            var entries = await client.ListAsync(directory);
            Assert.Contains(entries, entry => entry.Name == "中文 test.bin" && entry.Size == content.Length);

            const int offset = 4096;
            await using var destination = new MemoryStream();
            await destination.WriteAsync(content.AsMemory(0, offset));
            destination.Position = offset;
            await client.DownloadAsync(originalPath, destination, offset);
            Assert.Equal(content, destination.ToArray());

            await client.RenameAsync(originalPath, renamedPath);
            Assert.Equal(content.Length, await client.GetFileSizeAsync(renamedPath));
            await client.DeleteFileAsync(renamedPath);
            await client.DeleteDirectoryAsync(directory);
        }
        catch
        {
            try
            {
                await client.DeleteFileAsync(originalPath);
                await client.DeleteFileAsync(renamedPath);
                await client.DeleteDirectoryAsync(directory);
            }
            catch
            {
            }

            throw;
        }
    }

    [Fact]
    public async Task DockerMySql_InitializesAndPersistsSiteAndTask()
    {
        if (Environment.GetEnvironmentVariable("RUN_FTP_INTEGRATION") != "1")
        {
            return;
        }

        var database = new MySqlDatabase(new DatabaseOptions
        {
            ConnectionString =
                "Server=127.0.0.1;Port=3307;Database=ftp_client;User ID=ftp_app;Password=ftp_app_password;SslMode=None;AllowPublicKeyRetrieval=True;"
        });
        await database.InitializeAsync();

        var siteRepository = new MySqlSiteRepository(database);
        var site = new SiteProfile
        {
            Name = "integration",
            Host = "integration.local",
            Port = 21,
            Username = $"tester-{Guid.NewGuid():N}",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        site.Id = await siteRepository.UpsertAsync(site);
        Assert.True(site.Id > 0);
        Assert.Contains(await siteRepository.GetAllAsync(), item => item.Id == site.Id);

        var transferRepository = new MySqlTransferRepository(database);
        var task = new TransferTask
        {
            SiteId = site.Id,
            Direction = TransferDirection.Download,
            LocalPath = @"C:\temp\integration.bin",
            RemotePath = "/integration.bin",
            TotalBytes = 100,
            TransferredBytes = 25,
            Status = TransferStatus.Running
        };
        await transferRepository.UpsertAsync(task);

        var recovered = await transferRepository.GetRecoverableAsync();
        var restored = Assert.Single(recovered, item => item.Id == task.Id);
        Assert.Equal(TransferStatus.Paused, restored.Status);
        Assert.Equal(25, restored.TransferredBytes);

        await transferRepository.DeleteAsync(task.Id);
        await siteRepository.DeleteAsync(site.Id);
    }
}

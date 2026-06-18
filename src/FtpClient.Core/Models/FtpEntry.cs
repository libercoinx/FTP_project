namespace FtpClient.Core.Models;

public sealed record FtpEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long? Size,
    DateTimeOffset? ModifiedAt,
    string Permissions);

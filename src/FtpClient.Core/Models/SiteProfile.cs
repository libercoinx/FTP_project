namespace FtpClient.Core.Models;

public sealed class SiteProfile
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 21;
    public string Username { get; set; } = string.Empty;
    public string? ProtectedPassword { get; set; }
    public bool RememberPassword { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

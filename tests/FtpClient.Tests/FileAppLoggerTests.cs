using FtpClient.Infrastructure.Logging;

namespace FtpClient.Tests;

public sealed class FileAppLoggerTests
{
    [Theory]
    [InlineData("PASS secret-value", "PASS ***")]
    [InlineData("Server=x;Password=secret;Database=y", "Server=x;Password=***;Database=y")]
    [InlineData("Pwd=secret User=test", "Pwd=*** User=test")]
    public void Redact_RemovesPasswords(string input, string expected)
    {
        Assert.Equal(expected, FileAppLogger.Redact(input));
    }
}

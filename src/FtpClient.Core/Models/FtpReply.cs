namespace FtpClient.Core.Models;

public sealed record FtpReply(int Code, string Message)
{
    public bool IsPositivePreliminary => Code is >= 100 and < 200;
    public bool IsPositiveCompletion => Code is >= 200 and < 300;
    public bool IsPositiveIntermediate => Code is >= 300 and < 400;
    public bool IsSuccess => IsPositivePreliminary || IsPositiveCompletion || IsPositiveIntermediate;
}

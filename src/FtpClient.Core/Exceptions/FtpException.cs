namespace FtpClient.Core.Exceptions;

public sealed class FtpException : Exception
{
    public FtpException(string message, int? replyCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ReplyCode = replyCode;
    }

    public int? ReplyCode { get; }
}

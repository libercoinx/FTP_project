using FtpClient.Core.Models;

namespace FtpClient.Core.Interfaces;

public interface ISiteRepository
{
    Task<IReadOnlyList<SiteProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<long> UpsertAsync(SiteProfile site, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public interface ITransferRepository
{
    Task<IReadOnlyList<TransferTask>> GetRecoverableAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(TransferTask task, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ICredentialProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}

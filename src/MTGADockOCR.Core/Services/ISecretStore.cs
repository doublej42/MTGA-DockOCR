namespace MTGADockOCR.Core.Services;

public interface ISecretStore
{
    Task<string?> GetAsync(CancellationToken cancellationToken);

    Task SetAsync(string secret, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
using System.Security.Cryptography;
using System.Text;

namespace MTGADockOCR.Core.Services;

public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _filePath;

    public DpapiSecretStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var encrypted = await File.ReadAllBytesAsync(_filePath, cancellationToken);
        var plaintext = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintext);
    }

    public async Task SetAsync(string secret, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? throw new InvalidOperationException("A directory is required."));

        var normalizedSecret = secret.Trim();
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(normalizedSecret), optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_filePath, encrypted, cancellationToken);
        var persistedSecret = await GetAsync(cancellationToken);
        if (!string.Equals(normalizedSecret, persistedSecret, StringComparison.Ordinal))
        {
            throw new IOException("The encrypted API key could not be verified after saving.");
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }

        return Task.CompletedTask;
    }
}
namespace App.Services.Security;

public interface ISecretStore
{
    Task SaveSecretAsync(string key, string secret, CancellationToken cancellationToken = default);

    Task<string?> ReadSecretAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default);
}

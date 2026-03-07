namespace PatchHound.Infrastructure.Secrets;

public interface ISecretStore
{
    Task<string?> GetSecretAsync(string path, string key, CancellationToken ct);
    Task PutSecretAsync(string path, IReadOnlyDictionary<string, string> values, CancellationToken ct);
    Task<OpenBaoStatus> GetStatusAsync(CancellationToken ct);
}

namespace PatchHound.Infrastructure.Secrets;

public interface ISecretStore
{
    Task<string?> GetSecretAsync(string path, string key, CancellationToken ct);
    Task PutSecretAsync(
        string path,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct
    );
    Task DeleteSecretPathAsync(string path, CancellationToken ct);
    Task<OpenBaoStatus> GetStatusAsync(CancellationToken ct);
    Task<OpenBaoStatus> UnsealAsync(IReadOnlyList<string> keys, CancellationToken ct);
}

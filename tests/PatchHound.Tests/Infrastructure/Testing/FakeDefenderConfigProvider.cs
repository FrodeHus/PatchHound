using PatchHound.Infrastructure.VulnerabilitySources;

namespace PatchHound.Tests.Infrastructure.Testing;

public sealed class FakeDefenderConfigProvider : DefenderTenantConfigurationProvider
{
    private readonly DefenderClientConfiguration? _configuration;

    private FakeDefenderConfigProvider(DefenderClientConfiguration? configuration)
        : base(null!, null!)
    {
        _configuration = configuration;
    }

    public static FakeDefenderConfigProvider Default() =>
        new(new DefenderClientConfiguration(
            "00000000-0000-0000-0000-000000000001",
            "00000000-0000-0000-0000-000000000002",
            "fake-secret",
            "https://api.securitycenter.microsoft.com",
            "https://api.securitycenter.microsoft.com/.default"));

    public override Task<DefenderClientConfiguration?> GetConfigurationAsync(
        Guid tenantId,
        CancellationToken ct
    ) => Task.FromResult(_configuration);
}

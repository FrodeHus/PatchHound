using PatchHound.Core.Enums;
using PatchHound.Infrastructure.VulnerabilitySources;

namespace PatchHound.Tests.Infrastructure.Testing;

public sealed class FakeDefenderApiClient : DefenderApiClient
{
    private readonly DefenderMachineVulnerabilityResponse _machineVulnerabilities;

    private FakeDefenderApiClient(DefenderMachineVulnerabilityResponse machineVulnerabilities)
        : base(new HttpClient())
    {
        _machineVulnerabilities = machineVulnerabilities;
    }

    public static FakeDefenderApiClient WithSingleVulnerability(
        string cveId,
        Severity severity,
        decimal cvssScore)
    {
        var response = new DefenderMachineVulnerabilityResponse
        {
            Value =
            [
                new DefenderMachineVulnerabilityEntry
                {
                    Id = cveId,
                    CveId = cveId,
                    MachineId = "machine-1",
                    MachineName = "Server01",
                    ProductName = "Windows Server",
                    ProductVendor = "Microsoft",
                    ProductVersion = "2022",
                    Severity = severity.ToString(),
                    CvssV3 = cvssScore,
                },
            ],
        };
        return new FakeDefenderApiClient(response);
    }

    public override Task<DefenderMachineVulnerabilityResponse> GetMachineVulnerabilitiesAsync(
        DefenderClientConfiguration configuration,
        CancellationToken ct
    ) => Task.FromResult(_machineVulnerabilities);
}

namespace PatchHound.IngestionBenchmark;

public sealed record BenchmarkOptions(
    int TenantCount,
    int DevicesPerTenant,
    int VulnsPerDevice,
    int SoftwarePerDevice,
    int Runs)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        int Get(string key, int @default)
        {
            var hit = args.FirstOrDefault(a =>
                a.StartsWith($"--{key}=", StringComparison.OrdinalIgnoreCase));
            return hit is not null
                ? int.Parse(hit[(key.Length + 3)..])
                : @default;
        }

        return new BenchmarkOptions(
            TenantCount:       Get("tenants", 1),
            DevicesPerTenant:  Get("devices", 100),
            VulnsPerDevice:    Get("vulns-per-device", 10),
            SoftwarePerDevice: Get("software-per-device", 5),
            Runs:              Get("runs", 1));
    }

    public int TotalDevices => TenantCount * DevicesPerTenant;
    public int TotalStagedExposures => TotalDevices * VulnsPerDevice;
}

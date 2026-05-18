namespace PatchHound.IngestionBenchmark;

public enum SoftwareCatalogMode
{
    Shared,
    PerDevice,
}

public sealed record BenchmarkOptions(
    int TenantCount,
    int DevicesPerTenant,
    int VulnsPerDevice,
    int SoftwarePerDevice,
    int Runs,
    SoftwareCatalogMode SoftwareCatalog)
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

        SoftwareCatalogMode GetSoftwareCatalog()
        {
            var hit = args.FirstOrDefault(a =>
                a.StartsWith("--software-catalog=", StringComparison.OrdinalIgnoreCase));
            if (hit is null)
            {
                return SoftwareCatalogMode.Shared;
            }

            var value = hit["--software-catalog=".Length..]
                .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
            return Enum.TryParse<SoftwareCatalogMode>(value, ignoreCase: true, out var mode)
                ? mode
                : throw new ArgumentException(
                    "Invalid --software-catalog value. Use 'shared' or 'per-device'.");
        }

        return new BenchmarkOptions(
            TenantCount:       Get("tenants", 1),
            DevicesPerTenant:  Get("devices", 100),
            VulnsPerDevice:    Get("vulns-per-device", 10),
            SoftwarePerDevice: Get("software-per-device", 5),
            Runs:              Get("runs", 1),
            SoftwareCatalog:   GetSoftwareCatalog());
    }

    public int TotalDevices => TenantCount * DevicesPerTenant;
    public int TotalStagedExposures => TotalDevices * VulnsPerDevice;
    public int TotalStagedSoftware => SoftwareCatalog == SoftwareCatalogMode.Shared
        ? SoftwarePerDevice
        : TotalDevices * SoftwarePerDevice;
}

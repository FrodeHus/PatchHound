using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Tests.TestData;

/// <summary>
/// Lightweight factory helpers for canonical entities used across service/integration tests.
/// </summary>
internal static class CanonicalTestData
{
    private static readonly Guid DefaultSourceSystemId = Guid.NewGuid();

    public static SoftwareProduct Product(Guid? id = null)
    {
        var p = SoftwareProduct.Create(
            vendor: "TestVendor",
            name: "TestProduct",
            primaryCpe23Uri: null
        );
        if (id.HasValue)
            ForceSetId(p, id.Value);
        return p;
    }

    public static Device MakeDevice(Guid tenantId, Guid? sourceSystemId = null) =>
        Device.Create(
            tenantId,
            sourceSystemId ?? DefaultSourceSystemId,
            externalId: $"dev-{Guid.NewGuid():N}",
            name: "TestDevice",
            baselineCriticality: Criticality.Medium
        );

    public static InstalledSoftware MakeInstalledSoftware(Guid tenantId, Guid deviceId, Guid softwareProductId) =>
        InstalledSoftware.Observe(
            tenantId,
            deviceId,
            softwareProductId,
            sourceSystemId: DefaultSourceSystemId,
            version: "1.0.0",
            at: DateTimeOffset.UtcNow
        );

    /// <summary>
    /// Uses reflection to force-set the Id on a <see cref="SoftwareProduct"/>
    /// so tests can use a known Guid.
    /// </summary>
    private static void ForceSetId(SoftwareProduct product, Guid id)
    {
        var prop = typeof(SoftwareProduct).GetProperty(
            nameof(SoftwareProduct.Id),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        );
        prop?.SetValue(product, id);
    }
}

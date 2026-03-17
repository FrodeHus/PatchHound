using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Tests.Core;

public class AssetDeviceFieldsTests
{
    [Fact]
    public void UpdateDeviceDetails_SetsExposureLevelAndIsAadJoined()
    {
        var asset = Asset.Create(Guid.NewGuid(), "dev-1", AssetType.Device, "Test", Criticality.Medium);

        asset.UpdateDeviceDetails(
            "host.contoso.com", "Active", "Windows", "11",
            "Medium", DateTimeOffset.UtcNow, "10.0.0.1", "aad-1",
            "group-1", "Tier 0",
            "High", true
        );

        asset.DeviceExposureLevel.Should().Be("High");
        asset.DeviceIsAadJoined.Should().BeTrue();
    }

    [Fact]
    public void UpdateDeviceDetails_AllowsNullExposureLevelAndIsAadJoined()
    {
        var asset = Asset.Create(Guid.NewGuid(), "dev-2", AssetType.Device, "Test", Criticality.Medium);

        asset.UpdateDeviceDetails(
            "host.contoso.com", "Active", "Windows", "11",
            "Medium", DateTimeOffset.UtcNow, "10.0.0.1", "aad-1",
            "group-1", "Tier 0",
            null, null
        );

        asset.DeviceExposureLevel.Should().BeNull();
        asset.DeviceIsAadJoined.Should().BeNull();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class DeviceScanProfileAssignmentConfiguration : IEntityTypeConfiguration<DeviceScanProfileAssignment>
{
    public void Configure(EntityTypeBuilder<DeviceScanProfileAssignment> b)
    {
        b.HasKey(x => new { x.DeviceId, x.ScanProfileId });
        b.HasIndex(x => new { x.TenantId, x.ScanProfileId });
    }
}

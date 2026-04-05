using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class AssetScanProfileAssignmentConfiguration : IEntityTypeConfiguration<AssetScanProfileAssignment>
{
    public void Configure(EntityTypeBuilder<AssetScanProfileAssignment> b)
    {
        b.HasKey(x => new { x.AssetId, x.ScanProfileId });
        b.HasIndex(x => new { x.TenantId, x.ScanProfileId });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanProfileToolConfiguration : IEntityTypeConfiguration<ScanProfileTool>
{
    public void Configure(EntityTypeBuilder<ScanProfileTool> b)
    {
        b.HasKey(x => new { x.ScanProfileId, x.ScanningToolId });
    }
}

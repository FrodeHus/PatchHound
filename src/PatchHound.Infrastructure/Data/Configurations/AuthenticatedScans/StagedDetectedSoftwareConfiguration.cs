using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class StagedDetectedSoftwareConfiguration : IEntityTypeConfiguration<StagedDetectedSoftware>
{
    public void Configure(EntityTypeBuilder<StagedDetectedSoftware> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.CanonicalName).HasMaxLength(1024).IsRequired();
        b.Property(x => x.CanonicalProductKey).HasMaxLength(1024).IsRequired();
        b.Property(x => x.CanonicalVendor).HasMaxLength(1024);
        b.Property(x => x.Category).HasMaxLength(1024);
        b.Property(x => x.PrimaryCpe23Uri).HasMaxLength(1024);
        b.Property(x => x.DetectedVersion).HasMaxLength(256);
        b.HasIndex(x => x.ScanJobId);
    }
}

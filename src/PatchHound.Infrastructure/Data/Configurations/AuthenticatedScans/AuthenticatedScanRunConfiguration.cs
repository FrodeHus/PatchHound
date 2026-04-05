using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class AuthenticatedScanRunConfiguration : IEntityTypeConfiguration<AuthenticatedScanRun>
{
    public void Configure(EntityTypeBuilder<AuthenticatedScanRun> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.TriggerKind).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.ScanProfileId, x.StartedAt });
    }
}

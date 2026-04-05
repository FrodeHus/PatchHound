using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanRunnerConfiguration : IEntityTypeConfiguration<ScanRunner>
{
    public void Configure(EntityTypeBuilder<ScanRunner> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Description).HasColumnType("text").IsRequired();
        b.Property(x => x.SecretHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.Version).HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

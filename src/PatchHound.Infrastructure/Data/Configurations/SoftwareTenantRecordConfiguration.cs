using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SoftwareTenantRecordConfiguration : IEntityTypeConfiguration<SoftwareTenantRecord>
{
    public void Configure(EntityTypeBuilder<SoftwareTenantRecord> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => item.SnapshotId);
        builder.HasIndex(item => item.SoftwareProductId);
        builder.HasIndex(item => item.OwnerTeamId);
        builder
            .HasIndex(item => new { item.TenantId, item.SnapshotId, item.SoftwareProductId })
            .IsUnique();

        builder
            .HasOne(item => item.SoftwareProduct)
            .WithMany()
            .HasForeignKey(item => item.SoftwareProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

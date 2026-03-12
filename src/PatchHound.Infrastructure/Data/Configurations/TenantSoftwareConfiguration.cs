using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantSoftwareConfiguration : IEntityTypeConfiguration<TenantSoftware>
{
    public void Configure(EntityTypeBuilder<TenantSoftware> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => item.SnapshotId);
        builder.HasIndex(item => item.NormalizedSoftwareId);
        builder
            .HasIndex(item => new { item.TenantId, item.SnapshotId, item.NormalizedSoftwareId })
            .IsUnique();

        builder
            .HasOne(item => item.NormalizedSoftware)
            .WithMany()
            .HasForeignKey(item => item.NormalizedSoftwareId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

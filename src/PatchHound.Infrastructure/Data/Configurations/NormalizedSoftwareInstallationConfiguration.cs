using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class NormalizedSoftwareInstallationConfiguration
    : IEntityTypeConfiguration<NormalizedSoftwareInstallation>
{
    public void Configure(EntityTypeBuilder<NormalizedSoftwareInstallation> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => item.SnapshotId);
        builder.HasIndex(item => item.TenantSoftwareId);
        builder.HasIndex(item => item.SoftwareAssetId);
        builder.HasIndex(item => item.DeviceAssetId);
        builder
            .HasIndex(item => new
            {
                item.TenantId,
                item.SnapshotId,
                item.TenantSoftwareId,
                item.DetectedVersion,
                item.LastSeenAt,
            });
        builder
            .HasIndex(item => new
            {
                item.TenantId,
                item.SnapshotId,
                item.SoftwareAssetId,
                item.DeviceAssetId,
            })
            .IsUnique();

        builder.Property(item => item.SourceSystem).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.DetectedVersion).HasMaxLength(256);

        builder
            .HasOne(item => item.TenantSoftware)
            .WithMany()
            .HasForeignKey(item => item.TenantSoftwareId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(item => item.SoftwareAsset)
            .WithMany()
            .HasForeignKey(item => item.SoftwareAssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(item => item.DeviceAsset)
            .WithMany()
            .HasForeignKey(item => item.DeviceAssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

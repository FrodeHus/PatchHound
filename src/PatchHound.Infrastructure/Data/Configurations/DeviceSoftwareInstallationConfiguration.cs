using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class DeviceSoftwareInstallationConfiguration
    : IEntityTypeConfiguration<DeviceSoftwareInstallation>
{
    public void Configure(EntityTypeBuilder<DeviceSoftwareInstallation> builder)
    {
        builder.HasKey(link => link.Id);

        builder.HasIndex(link => link.TenantId);
        builder.HasIndex(link => new { link.DeviceAssetId, link.SoftwareAssetId }).IsUnique();

        builder
            .HasOne(link => link.DeviceAsset)
            .WithMany()
            .HasForeignKey(link => link.DeviceAssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(link => link.SoftwareAsset)
            .WithMany()
            .HasForeignKey(link => link.SoftwareAssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class DeviceSoftwareInstallationEpisodeConfiguration
    : IEntityTypeConfiguration<DeviceSoftwareInstallationEpisode>
{
    public void Configure(EntityTypeBuilder<DeviceSoftwareInstallationEpisode> builder)
    {
        builder.HasKey(episode => episode.Id);

        builder.HasIndex(episode => episode.TenantId);
        builder
            .HasIndex(episode => new
            {
                episode.DeviceAssetId,
                episode.SoftwareAssetId,
                episode.EpisodeNumber,
            })
            .IsUnique();

        builder
            .HasOne(episode => episode.DeviceAsset)
            .WithMany()
            .HasForeignKey(episode => episode.DeviceAssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(episode => episode.SoftwareAsset)
            .WithMany()
            .HasForeignKey(episode => episode.SoftwareAssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ExposureEpisodeConfiguration : IEntityTypeConfiguration<ExposureEpisode>
{
    public void Configure(EntityTypeBuilder<ExposureEpisode> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.DeviceVulnerabilityExposureId, x.EpisodeNumber }).IsUnique();

        builder.HasOne(x => x.Exposure)
            .WithMany()
            .HasForeignKey(x => x.DeviceVulnerabilityExposureId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

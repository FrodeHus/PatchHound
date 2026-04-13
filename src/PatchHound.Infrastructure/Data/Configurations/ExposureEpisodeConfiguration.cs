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
        builder.HasIndex(x => new { x.DeviceVulnerabilityExposureId, x.EpisodeNumber }).IsUnique();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();

        builder.HasOne(x => x.Exposure)
            .WithMany()
            .HasForeignKey(x => x.DeviceVulnerabilityExposureId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

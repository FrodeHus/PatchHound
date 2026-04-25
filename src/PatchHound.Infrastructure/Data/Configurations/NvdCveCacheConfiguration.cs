using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class NvdCveCacheConfiguration : IEntityTypeConfiguration<NvdCveCache>
{
    public void Configure(EntityTypeBuilder<NvdCveCache> builder)
    {
        builder.HasKey(e => e.CveId);
        builder.Property(e => e.CveId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Description).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(e => e.CvssScore).HasColumnType("numeric(4,2)");
        builder.Property(e => e.CvssVector).HasMaxLength(256);
        builder.Property(e => e.ReferencesJson).IsRequired().HasColumnType("text");
        builder.Property(e => e.ConfigurationsJson).IsRequired().HasColumnType("text");

        builder.HasIndex(e => e.PublishedDate);
        builder.HasIndex(e => e.FeedLastModified);
    }
}

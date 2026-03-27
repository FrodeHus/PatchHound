using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class NormalizedSoftwareConfiguration : IEntityTypeConfiguration<NormalizedSoftware>
{
    public void Configure(EntityTypeBuilder<NormalizedSoftware> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.CanonicalProductKey).IsUnique();

        builder.Property(item => item.CanonicalName).HasMaxLength(512).IsRequired();
        builder.Property(item => item.CanonicalVendor).HasMaxLength(256);
        builder.Property(item => item.CanonicalProductKey).HasMaxLength(512).IsRequired();
        builder.Property(item => item.PrimaryCpe23Uri).HasMaxLength(2048);
        builder.Property(item => item.Description).HasColumnType("text");
        builder.Property(item => item.DescriptionProviderType).HasMaxLength(64);
        builder.Property(item => item.DescriptionProfileName).HasMaxLength(256);
        builder.Property(item => item.DescriptionModel).HasMaxLength(256);
        builder.Property(item => item.NormalizationMethod).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.Confidence).HasConversion<string>().HasMaxLength(16);

        // End-of-life enrichment fields
        builder.Property(item => item.EolProductSlug).HasMaxLength(256);
        builder.Property(item => item.EolLatestVersion).HasMaxLength(128);
    }
}

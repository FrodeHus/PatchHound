using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class EnrichmentSourceConfigurationConfiguration
    : IEntityTypeConfiguration<EnrichmentSourceConfiguration>
{
    public void Configure(EntityTypeBuilder<EnrichmentSourceConfiguration> builder)
    {
        builder.HasKey(source => source.Id);

        builder.HasIndex(source => source.SourceKey).IsUnique();

        builder.Property(source => source.SourceKey).HasMaxLength(128).IsRequired();
        builder.Property(source => source.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(source => source.SecretRef).HasMaxLength(512).IsRequired();
        builder.Property(source => source.ApiBaseUrl).HasMaxLength(512).IsRequired();
        builder.Property(source => source.LastStatus).HasMaxLength(64).IsRequired();
        builder.Property(source => source.LastError).HasMaxLength(512).IsRequired();
    }
}

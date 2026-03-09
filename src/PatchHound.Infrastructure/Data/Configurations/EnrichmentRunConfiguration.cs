using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class EnrichmentRunConfiguration : IEntityTypeConfiguration<EnrichmentRun>
{
    public void Configure(EntityTypeBuilder<EnrichmentRun> builder)
    {
        builder.HasKey(run => run.Id);

        builder.HasIndex(run => run.SourceKey);
        builder.HasIndex(run => run.StartedAt);

        builder.Property(run => run.SourceKey).HasMaxLength(128).IsRequired();
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(run => run.LastError).HasMaxLength(1024).IsRequired();
    }
}

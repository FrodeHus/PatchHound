using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class IngestionRunConfiguration : IEntityTypeConfiguration<IngestionRun>
{
    public void Configure(EntityTypeBuilder<IngestionRun> builder)
    {
        builder.HasKey(run => run.Id);

        builder.HasIndex(run => new
        {
            run.TenantId,
            run.SourceKey,
            run.StartedAt,
        });

        builder.Property(run => run.SourceKey).HasMaxLength(128).IsRequired();
        builder.Property(run => run.Status).HasMaxLength(64).IsRequired();
        builder.Property(run => run.Error).HasMaxLength(512).IsRequired();
    }
}

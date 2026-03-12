using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class IngestionCheckpointConfiguration : IEntityTypeConfiguration<IngestionCheckpoint>
{
    public void Configure(EntityTypeBuilder<IngestionCheckpoint> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.IngestionRunId);
        builder.HasIndex(item => new { item.IngestionRunId, item.Phase }).IsUnique();
        builder.HasIndex(item => new { item.TenantId, item.SourceKey, item.Phase });

        builder.Property(item => item.SourceKey).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Phase).HasMaxLength(64).IsRequired();
        builder.Property(item => item.CursorJson).HasColumnType("text").IsRequired();
        builder.Property(item => item.Status).HasMaxLength(32).IsRequired();
    }
}

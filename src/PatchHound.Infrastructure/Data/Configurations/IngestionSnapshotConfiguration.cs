using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class IngestionSnapshotConfiguration : IEntityTypeConfiguration<IngestionSnapshot>
{
    public void Configure(EntityTypeBuilder<IngestionSnapshot> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => new
        {
            item.TenantId,
            item.SourceKey,
            item.Status,
        });

        builder.Property(item => item.SourceKey).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Status).HasMaxLength(32).IsRequired();
    }
}

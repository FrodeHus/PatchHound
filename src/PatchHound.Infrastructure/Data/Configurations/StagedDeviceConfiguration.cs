using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class StagedDeviceConfiguration : IEntityTypeConfiguration<StagedDevice>
{
    public void Configure(EntityTypeBuilder<StagedDevice> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.IngestionRunId);
        builder.HasIndex(item => new { item.IngestionRunId, item.BatchNumber });
        builder.HasIndex(item => new
        {
            item.TenantId,
            item.SourceKey,
            item.ExternalId,
        });

        builder.Property(item => item.SourceKey).HasMaxLength(128).IsRequired();
        builder.Property(item => item.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(512).IsRequired();
        builder.Property(item => item.AssetType).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.PayloadJson).HasColumnType("text").IsRequired();

        builder.ToTable("StagedDevices");
    }
}

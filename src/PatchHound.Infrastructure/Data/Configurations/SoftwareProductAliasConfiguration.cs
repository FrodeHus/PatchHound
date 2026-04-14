using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SoftwareProductAliasConfiguration
    : IEntityTypeConfiguration<SoftwareProductAlias>
{
    public void Configure(EntityTypeBuilder<SoftwareProductAlias> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => new { item.SourceSystem, item.ExternalSoftwareId }).IsUnique();
        builder.HasIndex(item => item.SoftwareProductId);

        builder.Property(item => item.SourceSystem).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.ExternalSoftwareId).HasMaxLength(512).IsRequired();
        builder.Property(item => item.RawName).HasMaxLength(512).IsRequired();
        builder.Property(item => item.RawVendor).HasMaxLength(256);
        builder.Property(item => item.RawVersion).HasMaxLength(256);
        builder.Property(item => item.AliasConfidence).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.MatchReason).HasMaxLength(1024).IsRequired();

        builder
            .HasOne(item => item.SoftwareProduct)
            .WithMany()
            .HasForeignKey(item => item.SoftwareProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

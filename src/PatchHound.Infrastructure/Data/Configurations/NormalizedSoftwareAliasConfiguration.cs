using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class NormalizedSoftwareAliasConfiguration
    : IEntityTypeConfiguration<NormalizedSoftwareAlias>
{
    public void Configure(EntityTypeBuilder<NormalizedSoftwareAlias> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.TenantId);
        builder
            .HasIndex(item => new { item.TenantId, item.SourceSystem, item.ExternalSoftwareId })
            .IsUnique();
        builder.HasIndex(item => item.NormalizedSoftwareId);

        builder.Property(item => item.SourceSystem).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.ExternalSoftwareId).HasMaxLength(512).IsRequired();
        builder.Property(item => item.RawName).HasMaxLength(512).IsRequired();
        builder.Property(item => item.RawVendor).HasMaxLength(256);
        builder.Property(item => item.RawVersion).HasMaxLength(256);
        builder.Property(item => item.AliasConfidence).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.MatchReason).HasMaxLength(1024).IsRequired();

        builder
            .HasOne(item => item.NormalizedSoftware)
            .WithMany()
            .HasForeignKey(item => item.NormalizedSoftwareId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

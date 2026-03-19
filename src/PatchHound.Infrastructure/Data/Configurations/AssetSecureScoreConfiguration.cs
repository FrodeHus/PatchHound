using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AssetSecureScoreConfiguration : IEntityTypeConfiguration<AssetSecureScore>
{
    public void Configure(EntityTypeBuilder<AssetSecureScore> builder)
    {
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => new { s.TenantId, s.AssetId }).IsUnique();
        builder.HasIndex(s => s.TenantId);

        builder.Property(s => s.OverallScore).HasPrecision(5, 1);
        builder.Property(s => s.VulnerabilityScore).HasPrecision(5, 1);
        builder.Property(s => s.ConfigurationScore).HasPrecision(5, 1);
        builder.Property(s => s.DeviceValueWeight).HasPrecision(4, 2);
        builder.Property(s => s.FactorsJson).HasColumnType("text");
        builder.Property(s => s.CalculationVersion).HasMaxLength(16).IsRequired();

        builder
            .HasOne(s => s.Asset)
            .WithMany()
            .HasForeignKey(s => s.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

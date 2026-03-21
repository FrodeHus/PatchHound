using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AssetRiskScoreConfiguration : IEntityTypeConfiguration<AssetRiskScore>
{
    public void Configure(EntityTypeBuilder<AssetRiskScore> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => new { item.TenantId, item.AssetId }).IsUnique();
        builder.HasIndex(item => item.TenantId);

        builder.Property(item => item.OverallScore).HasPrecision(7, 2);
        builder.Property(item => item.MaxEpisodeRiskScore).HasPrecision(7, 2);
        builder.Property(item => item.FactorsJson).HasColumnType("text").IsRequired();
        builder.Property(item => item.CalculationVersion).HasMaxLength(32).IsRequired();

        builder
            .HasOne(item => item.Asset)
            .WithMany()
            .HasForeignKey(item => item.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

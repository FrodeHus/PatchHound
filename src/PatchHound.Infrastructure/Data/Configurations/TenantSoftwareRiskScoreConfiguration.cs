using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantSoftwareRiskScoreConfiguration : IEntityTypeConfiguration<TenantSoftwareRiskScore>
{
    public void Configure(EntityTypeBuilder<TenantSoftwareRiskScore> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => item.TenantSoftwareId).IsUnique();
        builder.HasIndex(item => new { item.TenantId, item.SnapshotId });

        builder.Property(item => item.OverallScore).HasPrecision(7, 2);
        builder.Property(item => item.MaxEpisodeRiskScore).HasPrecision(7, 2);
        builder.Property(item => item.FactorsJson).HasColumnType("text").IsRequired();
        builder.Property(item => item.CalculationVersion).HasMaxLength(32).IsRequired();

        builder
            .HasOne(item => item.TenantSoftware)
            .WithMany()
            .HasForeignKey(item => item.TenantSoftwareId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

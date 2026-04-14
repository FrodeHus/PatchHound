using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SoftwareRiskScoreConfiguration : IEntityTypeConfiguration<SoftwareRiskScore>
{
    public void Configure(EntityTypeBuilder<SoftwareRiskScore> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => new { item.TenantId, item.SoftwareProductId }).IsUnique();
        builder.HasIndex(item => item.TenantId);

        builder.Property(item => item.OverallScore).HasPrecision(7, 2);
        builder.Property(item => item.MaxExposureScore).HasPrecision(7, 2);
        builder.Property(item => item.FactorsJson).HasColumnType("text").IsRequired();
        builder
            .Property(item => item.CalculationVersion)
            .HasMaxLength(SoftwareRiskScore.CalculationVersionMaxLength)
            .IsRequired();

        builder
            .HasOne(item => item.SoftwareProduct)
            .WithMany()
            .HasForeignKey(item => item.SoftwareProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

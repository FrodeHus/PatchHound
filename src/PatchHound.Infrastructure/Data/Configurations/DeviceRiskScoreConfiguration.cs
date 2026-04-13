using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class DeviceRiskScoreConfiguration : IEntityTypeConfiguration<DeviceRiskScore>
{
    public void Configure(EntityTypeBuilder<DeviceRiskScore> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => new { item.TenantId, item.DeviceId }).IsUnique();
        builder.HasIndex(item => item.TenantId);

        builder.Property(item => item.OverallScore).HasPrecision(7, 2);
        builder.Property(item => item.MaxEpisodeRiskScore).HasPrecision(7, 2);
        builder.Property(item => item.FactorsJson).HasColumnType("text").IsRequired();
        builder
            .Property(item => item.CalculationVersion)
            .HasMaxLength(DeviceRiskScore.CalculationVersionMaxLength)
            .IsRequired();

        builder
            .HasOne<Device>()
            .WithMany()
            .HasForeignKey(item => item.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

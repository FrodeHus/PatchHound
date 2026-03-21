using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class DeviceGroupRiskScoreConfiguration : IEntityTypeConfiguration<DeviceGroupRiskScore>
{
    public void Configure(EntityTypeBuilder<DeviceGroupRiskScore> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => new { item.TenantId, item.GroupKey }).IsUnique();

        builder.Property(item => item.GroupKey).HasMaxLength(256).IsRequired();
        builder.Property(item => item.DeviceGroupId).HasMaxLength(128);
        builder.Property(item => item.DeviceGroupName).HasMaxLength(256).IsRequired();
        builder.Property(item => item.OverallScore).HasPrecision(7, 2);
        builder.Property(item => item.MaxAssetRiskScore).HasPrecision(7, 2);
        builder.Property(item => item.FactorsJson).HasColumnType("text").IsRequired();
        builder.Property(item => item.CalculationVersion).HasMaxLength(32).IsRequired();
    }
}

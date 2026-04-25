using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class DeviceRuleConfiguration : IEntityTypeConfiguration<DeviceRule>
{
    public void Configure(EntityTypeBuilder<DeviceRule> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.AssetType).HasMaxLength(DeviceRule.AssetTypeMaxLength).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(DeviceRule.NameMaxLength).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(DeviceRule.DescriptionMaxLength);
        builder.Property(r => r.FilterDefinition).HasColumnType("text").IsRequired();
        builder.Property(r => r.Operations).HasColumnType("text").IsRequired();
        builder.HasIndex(r => new { r.TenantId, r.Priority }).IsUnique();
        builder.HasIndex(r => r.TenantId);
    }
}

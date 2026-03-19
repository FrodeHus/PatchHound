using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AssetRuleConfiguration : IEntityTypeConfiguration<AssetRule>
{
    public void Configure(EntityTypeBuilder<AssetRule> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(256).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(2048);
        builder.Property(r => r.FilterDefinition).HasColumnType("text").IsRequired();
        builder.Property(r => r.Operations).HasColumnType("text").IsRequired();
        builder.HasIndex(r => new { r.TenantId, r.Priority }).IsUnique();
        builder.HasIndex(r => r.TenantId);
    }
}

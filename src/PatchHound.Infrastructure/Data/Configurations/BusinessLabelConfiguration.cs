using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Infrastructure.Data.Configurations;

public class BusinessLabelConfiguration : IEntityTypeConfiguration<BusinessLabel>
{
    public void Configure(EntityTypeBuilder<BusinessLabel> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => new { item.TenantId, item.Name }).IsUnique();

        builder.Property(item => item.Name).HasMaxLength(128);
        builder.Property(item => item.Description).HasMaxLength(512);
        builder.Property(item => item.Color).HasMaxLength(32);
        builder.Property(item => item.WeightCategory)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(BusinessLabelWeightCategory.Normal);

        builder.Ignore(item => item.RiskWeight);
    }
}

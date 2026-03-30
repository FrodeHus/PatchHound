using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AssetBusinessLabelConfiguration : IEntityTypeConfiguration<AssetBusinessLabel>
{
    public void Configure(EntityTypeBuilder<AssetBusinessLabel> builder)
    {
        builder.HasKey(item => new { item.AssetId, item.BusinessLabelId, item.SourceKey });

        builder.HasIndex(item => item.BusinessLabelId);
        builder.HasIndex(item => item.AssignedByRuleId);

        builder.Property(item => item.SourceType).HasMaxLength(16);
        builder.Property(item => item.SourceKey).HasMaxLength(64);

        builder
            .HasOne(item => item.Asset)
            .WithMany()
            .HasForeignKey(item => item.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(item => item.BusinessLabel)
            .WithMany()
            .HasForeignKey(item => item.BusinessLabelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

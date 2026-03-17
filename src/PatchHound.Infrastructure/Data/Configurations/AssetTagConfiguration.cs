using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AssetTagConfiguration : IEntityTypeConfiguration<AssetTag>
{
    public void Configure(EntityTypeBuilder<AssetTag> builder)
    {
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => new { t.AssetId, t.Tag }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.Tag });

        builder.Property(t => t.Tag).HasMaxLength(256).IsRequired();
        builder.Property(t => t.Source).HasMaxLength(64).IsRequired();

        builder
            .HasOne<Asset>()
            .WithMany()
            .HasForeignKey(t => t.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

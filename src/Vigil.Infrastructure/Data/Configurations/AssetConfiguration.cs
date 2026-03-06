using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vigil.Core.Entities;

namespace Vigil.Infrastructure.Data.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.TenantId, a.ExternalId }).IsUnique();
        builder.HasIndex(a => a.TenantId);

        builder.Property(a => a.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(2048);
        builder.Property(a => a.AssetType).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Criticality).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.OwnerType).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Metadata).HasColumnType("text");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SoftwareProductConfiguration : IEntityTypeConfiguration<SoftwareProduct>
{
    public void Configure(EntityTypeBuilder<SoftwareProduct> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.CanonicalProductKey).IsUnique();
        builder.Property(p => p.CanonicalProductKey).HasMaxLength(512).IsRequired();
        builder.Property(p => p.Vendor).HasMaxLength(256).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(512).IsRequired();
        builder.Property(p => p.PrimaryCpe23Uri).HasMaxLength(512);
    }
}

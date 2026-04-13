using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SoftwareAliasConfiguration : IEntityTypeConfiguration<SoftwareAlias>
{
    public void Configure(EntityTypeBuilder<SoftwareAlias> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.SourceSystemId, a.ExternalId }).IsUnique();
        builder.HasIndex(a => a.SoftwareProductId);
        builder.Property(a => a.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(a => a.ObservedVendor).HasMaxLength(256);
        builder.Property(a => a.ObservedName).HasMaxLength(512);
        builder
            .HasOne<SoftwareProduct>()
            .WithMany()
            .HasForeignKey(a => a.SoftwareProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<SourceSystem>()
            .WithMany()
            .HasForeignKey(a => a.SourceSystemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

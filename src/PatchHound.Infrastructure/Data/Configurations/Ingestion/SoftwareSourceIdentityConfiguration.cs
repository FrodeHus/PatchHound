using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.Ingestion;

namespace PatchHound.Infrastructure.Data.Configurations.Ingestion;

public sealed class SoftwareSourceIdentityConfiguration : IEntityTypeConfiguration<SoftwareSourceIdentity>
{
    public void Configure(EntityTypeBuilder<SoftwareSourceIdentity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SourceSystemId, x.ExternalId })
            .IsUnique()
            .HasDatabaseName("UX_SoftwareSourceIdentities_Source_ExternalId");
        builder.HasIndex(x => x.SoftwareProductId);
        builder.HasIndex(x => x.SoftwareReleaseId);
        builder.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ObservedVendor).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ObservedName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ObservedVersion).HasMaxLength(128);
        builder.Property(x => x.CanonicalProductKey).HasMaxLength(512).IsRequired();
        builder.HasOne<SourceSystem>().WithMany().HasForeignKey(x => x.SourceSystemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SoftwareProduct>().WithMany().HasForeignKey(x => x.SoftwareProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SoftwareRelease>().WithMany().HasForeignKey(x => x.SoftwareReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

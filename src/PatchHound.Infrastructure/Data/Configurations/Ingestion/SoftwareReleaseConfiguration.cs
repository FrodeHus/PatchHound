using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.Ingestion;

namespace PatchHound.Infrastructure.Data.Configurations.Ingestion;

public sealed class SoftwareReleaseConfiguration : IEntityTypeConfiguration<SoftwareRelease>
{
    public void Configure(EntityTypeBuilder<SoftwareRelease> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SoftwareProductId, x.NormalizedVersion })
            .IsUnique()
            .HasDatabaseName("UX_SoftwareReleases_Product_Version");
        builder.Property(x => x.NormalizedVersion).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RawVersion).HasMaxLength(128).IsRequired();
        builder.HasOne<SoftwareProduct>().WithMany().HasForeignKey(x => x.SoftwareProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

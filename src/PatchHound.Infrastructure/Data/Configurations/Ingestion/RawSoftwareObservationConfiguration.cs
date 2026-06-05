using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.Ingestion;

namespace PatchHound.Infrastructure.Data.Configurations.Ingestion;

public sealed class RawSoftwareObservationConfiguration : IEntityTypeConfiguration<RawSoftwareObservation>
{
    public void Configure(EntityTypeBuilder<RawSoftwareObservation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.IngestionRunId, x.SourceSystemId, x.ExternalId })
            .IsUnique()
            .HasDatabaseName("UX_RawSoftwareObservations_Run_Source_ExternalId");
        builder.HasIndex(x => new { x.IngestionRunId, x.BatchNumber });
        builder.HasIndex(x => new { x.TenantId, x.SourceSystemId, x.ExternalId });
        builder.HasIndex(x => new { x.IngestionRunId, x.CanonicalProductKey });
        builder.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Vendor).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(128);
        builder.Property(x => x.CanonicalProductKey).HasMaxLength(512).IsRequired();
        builder.HasOne<IngestionRun>().WithMany().HasForeignKey(x => x.IngestionRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SourceSystem>().WithMany().HasForeignKey(x => x.SourceSystemId).OnDelete(DeleteBehavior.Restrict);
    }
}

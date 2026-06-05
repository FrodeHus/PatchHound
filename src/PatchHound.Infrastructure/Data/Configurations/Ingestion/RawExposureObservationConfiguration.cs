using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.Ingestion;

namespace PatchHound.Infrastructure.Data.Configurations.Ingestion;

public sealed class RawExposureObservationConfiguration : IEntityTypeConfiguration<RawExposureObservation>
{
    public void Configure(EntityTypeBuilder<RawExposureObservation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.IngestionRunId, x.SourceSystemId, x.DeviceExternalId, x.VulnerabilityExternalId, x.SoftwareExternalId })
            .IsUnique()
            .HasDatabaseName("UX_RawExposureObservations_Run_Device_Vulnerability_Software");
        builder.HasIndex(x => new { x.IngestionRunId, x.BatchNumber });
        builder.HasIndex(x => new { x.IngestionRunId, x.TenantId, x.DeviceExternalId });
        builder.HasIndex(x => new { x.IngestionRunId, x.TenantId, x.VulnerabilityExternalId });
        builder.Property(x => x.DeviceExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.VulnerabilityExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SoftwareExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.SoftwareVersion).HasMaxLength(128);
        builder.HasOne<IngestionRun>().WithMany().HasForeignKey(x => x.IngestionRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SourceSystem>().WithMany().HasForeignKey(x => x.SourceSystemId).OnDelete(DeleteBehavior.Restrict);
    }
}

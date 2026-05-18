using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.Ingestion;

namespace PatchHound.Infrastructure.Data.Configurations.Ingestion;

public sealed class RawInstallationObservationConfiguration : IEntityTypeConfiguration<RawInstallationObservation>
{
    public void Configure(EntityTypeBuilder<RawInstallationObservation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.IngestionRunId, x.SourceSystemId, x.DeviceExternalId, x.SoftwareExternalId })
            .IsUnique()
            .HasDatabaseName("UX_RawInstallationObservations_Run_Device_Software");
        builder.HasIndex(x => new { x.IngestionRunId, x.BatchNumber });
        builder.HasIndex(x => new { x.IngestionRunId, x.TenantId, x.DeviceExternalId });
        builder.HasIndex(x => new { x.IngestionRunId, x.TenantId, x.SoftwareExternalId });
        builder.Property(x => x.DeviceExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.SoftwareExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(128).IsRequired();
        builder.HasOne<IngestionRun>().WithMany().HasForeignKey(x => x.IngestionRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SourceSystem>().WithMany().HasForeignKey(x => x.SourceSystemId).OnDelete(DeleteBehavior.Restrict);
    }
}

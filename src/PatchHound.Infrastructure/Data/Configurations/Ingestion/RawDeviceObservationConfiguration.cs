using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.Ingestion;

namespace PatchHound.Infrastructure.Data.Configurations.Ingestion;

public sealed class RawDeviceObservationConfiguration : IEntityTypeConfiguration<RawDeviceObservation>
{
    public void Configure(EntityTypeBuilder<RawDeviceObservation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.IngestionRunId, x.SourceSystemId, x.ExternalId })
            .IsUnique()
            .HasDatabaseName("UX_RawDeviceObservations_Run_Source_ExternalId");
        builder.HasIndex(x => new { x.IngestionRunId, x.BatchNumber });
        builder.HasIndex(x => new { x.TenantId, x.SourceSystemId, x.ExternalId });
        builder.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ComputerDnsName).HasMaxLength(256);
        builder.Property(x => x.HealthStatus).HasMaxLength(64);
        builder.Property(x => x.OsPlatform).HasMaxLength(128);
        builder.Property(x => x.OsVersion).HasMaxLength(128);
        builder.HasOne<IngestionRun>().WithMany().HasForeignKey(x => x.IngestionRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SourceSystem>().WithMany().HasForeignKey(x => x.SourceSystemId).OnDelete(DeleteBehavior.Restrict);
    }
}

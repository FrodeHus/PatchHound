using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class StagedDeviceSoftwareInstallationConfiguration
    : IEntityTypeConfiguration<StagedDeviceSoftwareInstallation>
{
    public void Configure(EntityTypeBuilder<StagedDeviceSoftwareInstallation> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.IngestionRunId);
        builder.HasIndex(item => new
        {
            item.TenantId,
            item.SourceKey,
            item.DeviceExternalId,
            item.SoftwareExternalId,
        });

        builder.Property(item => item.SourceKey).HasMaxLength(128).IsRequired();
        builder.Property(item => item.DeviceExternalId).HasMaxLength(256).IsRequired();
        builder.Property(item => item.SoftwareExternalId).HasMaxLength(256).IsRequired();
        builder.Property(item => item.PayloadJson).HasColumnType("text").IsRequired();
    }
}

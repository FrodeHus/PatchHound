using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(d => d.Id);

        builder.HasIndex(d => new { d.TenantId, d.SourceSystemId, d.ExternalId }).IsUnique();
        builder.HasIndex(d => d.TenantId);
        builder.HasIndex(d => d.SecurityProfileId);
        builder.HasIndex(d => new { d.TenantId, d.ActiveInTenant });

        builder.Property(d => d.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(256).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(2048);
        builder.Property(d => d.ComputerDnsName).HasMaxLength(256);
        builder.Property(d => d.HealthStatus).HasMaxLength(64);
        builder.Property(d => d.OsPlatform).HasMaxLength(128);
        builder.Property(d => d.OsVersion).HasMaxLength(128);
        builder.Property(d => d.ExternalRiskLabel).HasMaxLength(64);
        builder.Property(d => d.LastIpAddress).HasMaxLength(128);
        builder.Property(d => d.AadDeviceId).HasMaxLength(128);
        builder.Property(d => d.GroupId).HasMaxLength(128);
        builder.Property(d => d.GroupName).HasMaxLength(256);
        builder.Property(d => d.ExposureLevel).HasMaxLength(64);
        builder.Property(d => d.OnboardingStatus).HasMaxLength(64);
        builder.Property(d => d.DeviceValue).HasMaxLength(64);
        builder.Property(d => d.ActiveInTenant).HasDefaultValue(true);
        builder.Property(d => d.BaselineCriticality).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Criticality).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.CriticalitySource).HasMaxLength(32);
        builder.Property(d => d.CriticalityReason).HasMaxLength(512);
        builder.Property(d => d.OwnerType).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Metadata).HasColumnType("text");

        builder
            .HasOne<SourceSystem>()
            .WithMany()
            .HasForeignKey(d => d.SourceSystemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

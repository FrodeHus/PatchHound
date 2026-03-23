using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.TenantId, a.ExternalId }).IsUnique();
        builder.HasIndex(a => a.TenantId);
        builder.HasIndex(a => a.SecurityProfileId);

        builder.Property(a => a.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(2048);
        builder.Property(a => a.DeviceComputerDnsName).HasMaxLength(256);
        builder.Property(a => a.DeviceHealthStatus).HasMaxLength(64);
        builder.Property(a => a.DeviceOsPlatform).HasMaxLength(128);
        builder.Property(a => a.DeviceOsVersion).HasMaxLength(128);
        builder.Property(a => a.DeviceRiskScore).HasMaxLength(64);
        builder.Property(a => a.DeviceLastIpAddress).HasMaxLength(128);
        builder.Property(a => a.DeviceAadDeviceId).HasMaxLength(128);
        builder.Property(a => a.DeviceGroupId).HasMaxLength(128);
        builder.Property(a => a.DeviceGroupName).HasMaxLength(256);
        builder.Property(a => a.DeviceExposureLevel).HasMaxLength(64);
        builder.Property(a => a.DeviceOnboardingStatus).HasMaxLength(64);
        builder.Property(a => a.DeviceValue).HasMaxLength(64);
        builder.Property(a => a.AssetType).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.BaselineCriticality).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Criticality).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.CriticalitySource).HasMaxLength(32);
        builder.Property(a => a.CriticalityReason).HasMaxLength(512);
        builder.Property(a => a.OwnerType).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Metadata).HasColumnType("text");

        builder
            .HasOne<AssetSecurityProfile>()
            .WithMany()
            .HasForeignKey(a => a.SecurityProfileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

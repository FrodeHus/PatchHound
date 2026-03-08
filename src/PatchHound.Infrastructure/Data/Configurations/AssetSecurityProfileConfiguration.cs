using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AssetSecurityProfileConfiguration : IEntityTypeConfiguration<AssetSecurityProfile>
{
    public void Configure(EntityTypeBuilder<AssetSecurityProfile> builder)
    {
        builder.HasKey(profile => profile.Id);

        builder.HasIndex(profile => new { profile.TenantId, profile.Name }).IsUnique();
        builder.HasIndex(profile => profile.TenantId);

        builder.Property(profile => profile.Name).HasMaxLength(256).IsRequired();
        builder.Property(profile => profile.Description).HasMaxLength(2048);
        builder
            .Property(profile => profile.EnvironmentClass)
            .HasConversion<string>()
            .HasMaxLength(32);
        builder
            .Property(profile => profile.InternetReachability)
            .HasConversion<string>()
            .HasMaxLength(32);
        builder
            .Property(profile => profile.ConfidentialityRequirement)
            .HasConversion<string>()
            .HasMaxLength(32);
        builder
            .Property(profile => profile.IntegrityRequirement)
            .HasConversion<string>()
            .HasMaxLength(32);
        builder
            .Property(profile => profile.AvailabilityRequirement)
            .HasConversion<string>()
            .HasMaxLength(32);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class OrganizationalSeverityConfiguration : IEntityTypeConfiguration<OrganizationalSeverity>
{
    public void Configure(EntityTypeBuilder<OrganizationalSeverity> builder)
    {
        builder.HasKey(os => os.Id);

        builder.HasIndex(os => new { os.TenantId, os.TenantVulnerabilityId }).IsUnique();

        builder.Property(os => os.AdjustedSeverity).HasConversion<string>().HasMaxLength(32);
        builder.Property(os => os.Justification).HasColumnType("text").IsRequired();
        builder.Property(os => os.AssetCriticalityFactor).HasMaxLength(512);
        builder.Property(os => os.ExposureFactor).HasMaxLength(512);
        builder.Property(os => os.CompensatingControls).HasMaxLength(1024);

        builder
            .HasOne(os => os.TenantVulnerability)
            .WithMany()
            .HasForeignKey(os => os.TenantVulnerabilityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

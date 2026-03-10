using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class RiskAcceptanceConfiguration : IEntityTypeConfiguration<RiskAcceptance>
{
    public void Configure(EntityTypeBuilder<RiskAcceptance> builder)
    {
        builder.HasKey(ra => ra.Id);

        builder.HasIndex(ra => ra.TenantId);
        builder.HasIndex(ra => ra.TenantVulnerabilityId);
        builder.HasIndex(ra => ra.Status);

        builder.Property(ra => ra.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(ra => ra.Justification).HasColumnType("text").IsRequired();
        builder.Property(ra => ra.Conditions).HasMaxLength(2048);
    }
}

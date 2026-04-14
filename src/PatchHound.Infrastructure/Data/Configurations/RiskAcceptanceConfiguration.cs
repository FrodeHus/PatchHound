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
        builder.HasIndex(ra => ra.Status);
        builder.HasIndex(ra => new { ra.TenantId, ra.RemediationCaseId, ra.Status });

        builder.Property(ra => ra.RemediationCaseId).IsRequired();
        builder.Property(ra => ra.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(ra => ra.Justification).HasColumnType("text").IsRequired();
        builder.Property(ra => ra.Conditions).HasMaxLength(2048);

        builder
            .HasOne(ra => ra.RemediationCase)
            .WithMany()
            .HasForeignKey(ra => ra.RemediationCaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

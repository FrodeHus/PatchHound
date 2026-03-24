using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class RemediationDecisionConfiguration : IEntityTypeConfiguration<RemediationDecision>
{
    public void Configure(EntityTypeBuilder<RemediationDecision> builder)
    {
        builder.HasKey(rd => rd.Id);

        builder.HasIndex(rd => rd.TenantId);
        builder.HasIndex(rd => new { rd.TenantId, rd.TenantSoftwareId });
        builder.HasIndex(rd => rd.ApprovalStatus);

        builder.Property(rd => rd.Outcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(rd => rd.ApprovalStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(rd => rd.Justification).HasColumnType("text");

        builder
            .HasOne(rd => rd.SoftwareAsset)
            .WithMany()
            .HasForeignKey(rd => rd.SoftwareAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasMany(rd => rd.VulnerabilityOverrides)
            .WithOne(vo => vo.RemediationDecision)
            .HasForeignKey(vo => vo.RemediationDecisionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

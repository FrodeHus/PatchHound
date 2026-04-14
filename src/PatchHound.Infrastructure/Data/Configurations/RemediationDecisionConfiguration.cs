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
        builder.HasIndex(rd => rd.ApprovalStatus);
        builder.HasIndex(rd => rd.RemediationWorkflowId);
        builder.HasIndex(rd => new { rd.TenantId, rd.RemediationCaseId, rd.ApprovalStatus });

        builder.Property(rd => rd.RemediationCaseId).IsRequired();
        builder.Property(rd => rd.Outcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(rd => rd.ApprovalStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(rd => rd.Justification).HasColumnType("text");

        builder
            .HasOne(rd => rd.RemediationCase)
            .WithMany()
            .HasForeignKey(rd => rd.RemediationCaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(rd => rd.RemediationWorkflow)
            .WithMany()
            .HasForeignKey(rd => rd.RemediationWorkflowId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasMany(rd => rd.VulnerabilityOverrides)
            .WithOne(vo => vo.RemediationDecision)
            .HasForeignKey(vo => vo.RemediationDecisionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

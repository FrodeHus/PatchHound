using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ApprovalTaskConfiguration : IEntityTypeConfiguration<ApprovalTask>
{
    public void Configure(EntityTypeBuilder<ApprovalTask> builder)
    {
        builder.HasKey(at => at.Id);

        builder.HasIndex(at => at.TenantId);
        builder.HasIndex(at => new { at.TenantId, at.RemediationDecisionId });
        builder.HasIndex(at => at.Status);
        builder.HasIndex(at => at.ExpiresAt);
        builder.HasIndex(at => at.RemediationWorkflowId);
        builder.HasIndex(at => new { at.TenantId, at.RemediationCaseId, at.Status });

        builder.Property(at => at.RemediationCaseId).IsRequired();

        builder.Property(at => at.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(at => at.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(at => at.ResolutionJustification).HasColumnType("text");

        builder
            .HasOne(at => at.RemediationCase)
            .WithMany()
            .HasForeignKey(at => at.RemediationCaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(at => at.RemediationDecision)
            .WithMany()
            .HasForeignKey(at => at.RemediationDecisionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(at => at.RemediationWorkflow)
            .WithMany()
            .HasForeignKey(at => at.RemediationWorkflowId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Navigation(at => at.VisibleRoles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

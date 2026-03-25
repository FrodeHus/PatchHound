using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class RemediationWorkflowConfiguration : IEntityTypeConfiguration<RemediationWorkflow>
{
    public void Configure(EntityTypeBuilder<RemediationWorkflow> builder)
    {
        builder.HasKey(workflow => workflow.Id);

        builder.HasIndex(workflow => workflow.TenantId);
        builder.HasIndex(workflow => new { workflow.TenantId, workflow.TenantSoftwareId });
        builder.HasIndex(workflow => new { workflow.TenantId, workflow.Status });
        builder.HasIndex(workflow => workflow.RecurrenceSourceWorkflowId);

        builder.Property(workflow => workflow.CurrentStage).HasConversion<string>().HasMaxLength(32);
        builder.Property(workflow => workflow.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(workflow => workflow.ProposedOutcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(workflow => workflow.Priority).HasConversion<string>().HasMaxLength(32);
        builder.Property(workflow => workflow.ApprovalMode).HasConversion<string>().HasMaxLength(32);

        builder
            .HasMany(workflow => workflow.StageRecords)
            .WithOne(record => record.Workflow)
            .HasForeignKey(record => record.RemediationWorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class WorkflowActionConfiguration : IEntityTypeConfiguration<WorkflowAction>
{
    public void Configure(EntityTypeBuilder<WorkflowAction> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.TenantId, e.TeamId, e.Status });
        builder.HasIndex(e => e.NodeExecutionId).IsUnique();

        builder.Property(e => e.ActionType).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Instructions).HasMaxLength(4000);
        builder.Property(e => e.ResponseJson).HasColumnType("jsonb");

        builder
            .HasOne(e => e.WorkflowInstance)
            .WithMany()
            .HasForeignKey(e => e.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.NodeExecution)
            .WithMany()
            .HasForeignKey(e => e.NodeExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

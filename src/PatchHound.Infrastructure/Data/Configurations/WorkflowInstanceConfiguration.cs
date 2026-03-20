using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.Status });
        builder.HasIndex(e => new { e.TenantId, e.Status });

        builder.Property(e => e.TriggerType).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.ContextJson).HasColumnType("jsonb");
        builder.Property(e => e.Error).HasMaxLength(4000);

        builder
            .HasOne(e => e.WorkflowDefinition)
            .WithMany()
            .HasForeignKey(e => e.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasMany(e => e.NodeExecutions)
            .WithOne(e => e.WorkflowInstance)
            .HasForeignKey(e => e.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

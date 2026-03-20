using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class WorkflowNodeExecutionConfiguration : IEntityTypeConfiguration<WorkflowNodeExecution>
{
    public void Configure(EntityTypeBuilder<WorkflowNodeExecution> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.WorkflowInstanceId, e.NodeId });
        builder.HasIndex(e => new { e.WorkflowInstanceId, e.Status });

        builder.Property(e => e.NodeId).HasMaxLength(256).IsRequired();
        builder.Property(e => e.NodeType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.InputJson).HasColumnType("jsonb");
        builder.Property(e => e.OutputJson).HasColumnType("jsonb");
        builder.Property(e => e.Error).HasMaxLength(4000);
    }
}

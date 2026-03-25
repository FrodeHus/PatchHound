using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class RemediationWorkflowStageRecordConfiguration : IEntityTypeConfiguration<RemediationWorkflowStageRecord>
{
    public void Configure(EntityTypeBuilder<RemediationWorkflowStageRecord> builder)
    {
        builder.HasKey(record => record.Id);

        builder.HasIndex(record => record.TenantId);
        builder.HasIndex(record => new { record.TenantId, record.RemediationWorkflowId });
        builder.HasIndex(record => new { record.RemediationWorkflowId, record.Stage });

        builder.Property(record => record.Stage).HasConversion<string>().HasMaxLength(32);
        builder.Property(record => record.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(record => record.AssignedRole).HasConversion<string>().HasMaxLength(32);
        builder.Property(record => record.Summary).HasColumnType("text");
    }
}

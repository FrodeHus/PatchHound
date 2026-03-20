using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.TenantId, e.Scope, e.TriggerType });
        builder.HasIndex(e => new { e.Scope, e.Status });

        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Scope).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.TriggerType).HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.GraphJson).HasColumnType("jsonb");
    }
}

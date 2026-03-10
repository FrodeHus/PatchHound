using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class RemediationTaskConfiguration : IEntityTypeConfiguration<RemediationTask>
{
    public void Configure(EntityTypeBuilder<RemediationTask> builder)
    {
        builder.HasKey(rt => rt.Id);

        builder.HasIndex(rt => rt.TenantId);
        builder.HasIndex(rt => rt.TenantVulnerabilityId);
        builder.HasIndex(rt => rt.Status);
        builder.HasIndex(rt => rt.AssigneeId);

        builder.Property(rt => rt.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(rt => rt.Justification).HasMaxLength(2048);
    }
}

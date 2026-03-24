using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class PatchingTaskConfiguration : IEntityTypeConfiguration<PatchingTask>
{
    public void Configure(EntityTypeBuilder<PatchingTask> builder)
    {
        builder.HasKey(pt => pt.Id);

        builder.HasIndex(pt => pt.TenantId);
        builder.HasIndex(pt => new { pt.TenantId, pt.TenantSoftwareId });
        builder.HasIndex(pt => pt.OwnerTeamId);
        builder.HasIndex(pt => pt.Status);

        builder.Property(pt => pt.Status).HasConversion<string>().HasMaxLength(32);

        builder
            .HasOne(pt => pt.RemediationDecision)
            .WithMany()
            .HasForeignKey(pt => pt.RemediationDecisionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

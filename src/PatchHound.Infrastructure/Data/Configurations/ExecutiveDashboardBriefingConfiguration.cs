using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ExecutiveDashboardBriefingConfiguration
    : IEntityTypeConfiguration<ExecutiveDashboardBriefing>
{
    public void Configure(EntityTypeBuilder<ExecutiveDashboardBriefing> builder)
    {
        builder.HasKey(item => item.TenantId);

        builder.Property(item => item.Content).HasColumnType("text").IsRequired();
        builder.Property(item => item.GeneratedAt).IsRequired();
        builder.Property(item => item.WindowStartedAt).IsRequired();
        builder.Property(item => item.WindowEndedAt).IsRequired();
        builder.Property(item => item.HighCriticalAppearedCount).IsRequired();
        builder.Property(item => item.ResolvedCount).IsRequired();
        builder.Property(item => item.UsedAi).IsRequired();

        builder
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(item => item.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

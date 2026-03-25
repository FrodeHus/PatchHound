using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AnalystRecommendationConfiguration : IEntityTypeConfiguration<AnalystRecommendation>
{
    public void Configure(EntityTypeBuilder<AnalystRecommendation> builder)
    {
        builder.HasKey(ar => ar.Id);

        builder.HasIndex(ar => ar.TenantId);
        builder.HasIndex(ar => new { ar.TenantId, ar.SoftwareAssetId });
        builder.HasIndex(ar => ar.RemediationWorkflowId);

        builder.Property(ar => ar.RecommendedOutcome).HasConversion<string>().HasMaxLength(32);
        builder.Property(ar => ar.Rationale).HasColumnType("text").IsRequired();
        builder.Property(ar => ar.PriorityOverride).HasMaxLength(64);

        builder
            .HasOne(ar => ar.RemediationWorkflow)
            .WithMany()
            .HasForeignKey(ar => ar.RemediationWorkflowId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

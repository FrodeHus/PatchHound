using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class RecommendationContextSnapshotConfiguration
    : IEntityTypeConfiguration<RecommendationContextSnapshot>
{
    public void Configure(EntityTypeBuilder<RecommendationContextSnapshot> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TenantId, x.RemediationCaseId });
        builder.Property(x => x.ContextJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.ContextHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CitationsJson).HasColumnType("text").IsRequired();
    }
}

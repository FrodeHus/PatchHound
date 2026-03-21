using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantRiskScoreSnapshotConfiguration : IEntityTypeConfiguration<TenantRiskScoreSnapshot>
{
    public void Configure(EntityTypeBuilder<TenantRiskScoreSnapshot> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => new { item.TenantId, item.Date }).IsUnique();

        builder.Property(item => item.OverallScore).HasPrecision(7, 2);
    }
}

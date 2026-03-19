using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantSecureScoreSnapshotConfiguration : IEntityTypeConfiguration<TenantSecureScoreSnapshot>
{
    public void Configure(EntityTypeBuilder<TenantSecureScoreSnapshot> builder)
    {
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => new { s.TenantId, s.Date }).IsUnique();
        builder.HasIndex(s => s.TenantId);

        builder.Property(s => s.OverallScore).HasPrecision(5, 1);
    }
}

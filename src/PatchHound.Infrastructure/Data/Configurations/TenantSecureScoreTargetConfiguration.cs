using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantSecureScoreTargetConfiguration : IEntityTypeConfiguration<TenantSecureScoreTarget>
{
    public void Configure(EntityTypeBuilder<TenantSecureScoreTarget> builder)
    {
        builder.HasKey(t => t.TenantId);

        builder.Property(t => t.TargetScore).HasPrecision(5, 1);
    }
}

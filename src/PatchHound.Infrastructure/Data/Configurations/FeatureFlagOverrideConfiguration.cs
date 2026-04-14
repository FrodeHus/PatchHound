using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class FeatureFlagOverrideConfiguration : IEntityTypeConfiguration<FeatureFlagOverride>
{
    public void Configure(EntityTypeBuilder<FeatureFlagOverride> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.FlagName).HasMaxLength(256).IsRequired();
        builder.Property(f => f.TenantId).IsRequired(false);
        builder.Property(f => f.UserId).IsRequired(false);
        builder.Property(f => f.IsEnabled).IsRequired();
        builder.Property(f => f.CreatedAt).IsRequired();
        builder.Property(f => f.ExpiresAt).IsRequired(false);

        builder.HasIndex(f => new { f.FlagName, f.TenantId });
        builder.HasIndex(f => new { f.FlagName, f.UserId });

        // Exactly one of TenantId/UserId must be non-null
        builder.ToTable(
            "FeatureFlagOverrides",
            t => t.HasCheckConstraint(
                "CK_FeatureFlagOverrides_OneTarget",
                "(\"TenantId\" IS NOT NULL AND \"UserId\" IS NULL) OR (\"TenantId\" IS NULL AND \"UserId\" IS NOT NULL)"
            )
        );

        builder
            .HasOne(f => f.Tenant)
            .WithMany()
            .HasForeignKey(f => f.TenantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

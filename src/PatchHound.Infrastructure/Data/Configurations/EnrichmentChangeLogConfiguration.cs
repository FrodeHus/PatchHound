using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class EnrichmentChangeLogConfiguration : IEntityTypeConfiguration<EnrichmentChangeLog>
{
    public void Configure(EntityTypeBuilder<EnrichmentChangeLog> builder)
    {
        builder.ToTable(
            "EnrichmentChangeLog",
            table => table.HasCheckConstraint(
                "CK_EnrichmentChangeLog_Scope_TenantId",
                "(\"Scope\" = 'Global' AND \"TenantId\" IS NULL) OR (\"Scope\" = 'Tenant' AND \"TenantId\" IS NOT NULL)"
            )
        );

        builder.HasKey(change => change.Id);

        builder.HasIndex(change => new
        {
            change.Scope,
            change.TenantId,
            change.EntityType,
            change.EntityId,
            change.ChangedAt,
        });
        builder.HasIndex(change => new
        {
            change.Scope,
            change.TenantId,
            change.SourceKey,
            change.ChangedAt,
        });
        builder.HasIndex(change => change.EnrichmentRunId);
        builder.HasIndex(change => change.EnrichmentJobId);

        builder.Property(change => change.Scope)
            .HasConversion<string>()
            .HasMaxLength(EnrichmentChangeLog.ScopeMaxLength);
        builder.Property(change => change.EntityType)
            .HasMaxLength(EnrichmentChangeLog.EntityTypeMaxLength)
            .IsRequired();
        builder.Property(change => change.SourceKey)
            .HasMaxLength(EnrichmentChangeLog.SourceKeyMaxLength)
            .IsRequired();
        builder.Property(change => change.FieldPath)
            .HasMaxLength(EnrichmentChangeLog.FieldPathMaxLength)
            .IsRequired();
        builder.Property(change => change.DisplayName)
            .HasMaxLength(EnrichmentChangeLog.DisplayNameMaxLength)
            .IsRequired();
        builder.Property(change => change.OldValueJson).HasColumnType("text");
        builder.Property(change => change.NewValueJson).HasColumnType("text");
        builder.Property(change => change.ValueKind)
            .HasConversion<string>()
            .HasMaxLength(EnrichmentChangeLog.ValueKindMaxLength);
        builder.Property(change => change.ChangeReason)
            .HasMaxLength(EnrichmentChangeLog.ChangeReasonMaxLength);
        builder.Property(change => change.Confidence).HasPrecision(6, 4);
    }
}

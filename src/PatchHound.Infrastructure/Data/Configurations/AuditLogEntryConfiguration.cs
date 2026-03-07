using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => a.TenantId);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.Timestamp);

        builder.Property(a => a.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.OldValues).HasColumnType("text");
        builder.Property(a => a.NewValues).HasColumnType("text");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vigil.Core.Entities;

namespace Vigil.Infrastructure.Data.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => t.EntraTenantId).IsUnique();

        builder.Property(t => t.Name).HasMaxLength(256).IsRequired();
        builder.Property(t => t.EntraTenantId).HasMaxLength(256).IsRequired();
        builder.Property(t => t.Settings).HasColumnType("text");
    }
}

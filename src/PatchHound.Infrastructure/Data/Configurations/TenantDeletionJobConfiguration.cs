using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantDeletionJobConfiguration : IEntityTypeConfiguration<TenantDeletionJob>
{
    public void Configure(EntityTypeBuilder<TenantDeletionJob> builder)
    {
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(j => j.Error).HasMaxLength(2048);
        builder.HasIndex(j => j.TenantId).IsUnique();
        builder.HasIndex(j => j.Status);
    }
}

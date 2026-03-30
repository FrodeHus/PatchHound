using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class RemediationAiJobConfiguration : IEntityTypeConfiguration<RemediationAiJob>
{
    public void Configure(EntityTypeBuilder<RemediationAiJob> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => new { item.TenantId, item.TenantSoftwareId, item.RequestedAt });
        builder.HasIndex(item => new { item.TenantId, item.TenantSoftwareId, item.Status });

        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.InputHash).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Error).HasMaxLength(2048).IsRequired();
    }
}

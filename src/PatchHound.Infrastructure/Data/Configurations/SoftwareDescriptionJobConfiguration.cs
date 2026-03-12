using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SoftwareDescriptionJobConfiguration : IEntityTypeConfiguration<SoftwareDescriptionJob>
{
    public void Configure(EntityTypeBuilder<SoftwareDescriptionJob> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => new { item.TenantId, item.TenantSoftwareId, item.RequestedAt });
        builder.HasIndex(item => new { item.TenantId, item.TenantSoftwareId, item.Status });

        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.Error).HasMaxLength(2048).IsRequired();
    }
}

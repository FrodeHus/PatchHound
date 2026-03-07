using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.HasKey(c => c.Id);

        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => c.Status);

        builder.Property(c => c.Name).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(2048);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);

        builder.HasMany(c => c.Vulnerabilities).WithOne().HasForeignKey(cv => cv.CampaignId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AIReportConfiguration : IEntityTypeConfiguration<AIReport>
{
    public void Configure(EntityTypeBuilder<AIReport> builder)
    {
        builder.HasKey(r => r.Id);

        builder.HasIndex(r => r.TenantId);
        builder.HasIndex(r => r.TenantVulnerabilityId);

        builder.Property(r => r.Content).HasColumnType("text").IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(128).IsRequired();

        builder
            .HasOne(r => r.TenantVulnerability)
            .WithMany()
            .HasForeignKey(r => r.TenantVulnerabilityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

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
        builder.HasIndex(r => r.TenantAiProfileId);
        builder.HasIndex(r => r.VulnerabilityId);
        builder.HasIndex(r => new { r.TenantId, r.RemediationCaseId, r.GeneratedAt });

        builder.Property(r => r.RemediationCaseId).IsRequired();
        builder.Property(r => r.Content).HasColumnType("text").IsRequired();
        builder.Property(r => r.ProviderType).HasMaxLength(32).IsRequired();
        builder.Property(r => r.ProfileName).HasMaxLength(256).IsRequired();
        builder.Property(r => r.Model).HasMaxLength(256).IsRequired();
        builder.Property(r => r.SystemPromptHash).HasMaxLength(128).IsRequired();
        builder.Property(r => r.Temperature).HasPrecision(4, 2);

        builder
            .HasOne(r => r.RemediationCase)
            .WithMany()
            .HasForeignKey(r => r.RemediationCaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(r => r.Vulnerability)
            .WithMany()
            .HasForeignKey(r => r.VulnerabilityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(r => r.TenantAiProfile)
            .WithMany()
            .HasForeignKey(r => r.TenantAiProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

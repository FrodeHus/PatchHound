using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantAiProfileConfiguration : IEntityTypeConfiguration<TenantAiProfile>
{
    public void Configure(EntityTypeBuilder<TenantAiProfile> builder)
    {
        builder.HasKey(item => item.Id);

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => new { item.TenantId, item.Name }).IsUnique();

        builder.Property(item => item.Name).HasMaxLength(256).IsRequired();
        builder.Property(item => item.ProviderType).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.Model).HasMaxLength(256).IsRequired();
        builder.Property(item => item.SystemPrompt).HasColumnType("text").IsRequired();
        builder.Property(item => item.BaseUrl).HasMaxLength(512).IsRequired();
        builder.Property(item => item.DeploymentName).HasMaxLength(256).IsRequired();
        builder.Property(item => item.ApiVersion).HasMaxLength(64).IsRequired();
        builder.Property(item => item.KeepAlive).HasMaxLength(64).IsRequired();
        builder.Property(item => item.SecretRef).HasMaxLength(512).IsRequired();
        builder
            .Property(item => item.WebResearchMode)
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(item => item.AllowedDomains).HasColumnType("text").IsRequired();
        builder
            .Property(item => item.LastValidationStatus)
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(item => item.LastValidationError).HasMaxLength(1024).IsRequired();
        builder.Property(item => item.Temperature).HasPrecision(4, 2);
        builder.Property(item => item.TopP).HasPrecision(4, 2);
    }
}

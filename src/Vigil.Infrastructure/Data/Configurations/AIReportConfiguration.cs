using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vigil.Core.Entities;

namespace Vigil.Infrastructure.Data.Configurations;

public class AIReportConfiguration : IEntityTypeConfiguration<AIReport>
{
    public void Configure(EntityTypeBuilder<AIReport> builder)
    {
        builder.HasKey(r => r.Id);

        builder.HasIndex(r => r.TenantId);
        builder.HasIndex(r => r.VulnerabilityId);

        builder.Property(r => r.Content).HasColumnType("text").IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(128).IsRequired();
    }
}

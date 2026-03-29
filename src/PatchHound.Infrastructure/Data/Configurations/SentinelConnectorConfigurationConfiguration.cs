using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SentinelConnectorConfigurationConfiguration
    : IEntityTypeConfiguration<SentinelConnectorConfiguration>
{
    public void Configure(EntityTypeBuilder<SentinelConnectorConfiguration> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.DceEndpoint).HasMaxLength(512);
        builder.Property(c => c.DcrImmutableId).HasMaxLength(256);
        builder.Property(c => c.StreamName).HasMaxLength(256);
        builder.Property(c => c.TenantId).HasMaxLength(128);
        builder.Property(c => c.ClientId).HasMaxLength(128);
        builder.Property(c => c.SecretRef).HasMaxLength(256);
    }
}

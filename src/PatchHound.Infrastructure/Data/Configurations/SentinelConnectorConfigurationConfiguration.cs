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

        builder.HasOne(c => c.StoredCredential)
            .WithMany()
            .HasForeignKey(c => c.StoredCredentialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

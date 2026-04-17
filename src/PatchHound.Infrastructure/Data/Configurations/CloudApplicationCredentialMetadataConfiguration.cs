using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class CloudApplicationCredentialMetadataConfiguration
    : IEntityTypeConfiguration<CloudApplicationCredentialMetadata>
{
    public void Configure(EntityTypeBuilder<CloudApplicationCredentialMetadata> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.CloudApplicationId);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.ExpiresAt);

        builder.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(512);

        builder.ToTable("CloudApplicationCredentialMetadata");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class StoredCredentialConfiguration : IEntityTypeConfiguration<StoredCredential>
{
    public void Configure(EntityTypeBuilder<StoredCredential> builder)
    {
        builder.HasKey(credential => credential.Id);

        builder.Property(credential => credential.Name).HasMaxLength(160).IsRequired();
        builder.Property(credential => credential.Type).HasMaxLength(80).IsRequired();
        builder.Property(credential => credential.CredentialTenantId).HasMaxLength(256);
        builder.Property(credential => credential.ClientId).HasMaxLength(256);
        builder.Property(credential => credential.SecretRef).HasMaxLength(512).IsRequired();

        builder.HasIndex(credential => credential.Type);
        builder.HasIndex(credential => credential.IsGlobal);
    }
}

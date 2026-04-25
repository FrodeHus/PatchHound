using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class StoredCredentialTenantConfiguration : IEntityTypeConfiguration<StoredCredentialTenant>
{
    public void Configure(EntityTypeBuilder<StoredCredentialTenant> builder)
    {
        builder.HasKey(scope => new { scope.StoredCredentialId, scope.TenantId });

        builder.HasOne(scope => scope.StoredCredential)
            .WithMany(credential => credential.TenantScopes)
            .HasForeignKey(scope => scope.StoredCredentialId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(scope => scope.Tenant)
            .WithMany()
            .HasForeignKey(scope => scope.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(scope => scope.TenantId);
    }
}

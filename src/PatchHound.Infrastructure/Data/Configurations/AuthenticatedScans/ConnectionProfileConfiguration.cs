using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ConnectionProfileConfiguration : IEntityTypeConfiguration<ConnectionProfile>
{
    public void Configure(EntityTypeBuilder<ConnectionProfile> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Description).HasColumnType("text").IsRequired();
        b.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        b.Property(x => x.SshHost).HasMaxLength(512).IsRequired();
        b.Property(x => x.SshUsername).HasMaxLength(256).IsRequired();
        b.Property(x => x.AuthMethod).HasMaxLength(32).IsRequired();
        b.Property(x => x.SecretRef).HasMaxLength(512).IsRequired();
        b.Property(x => x.HostKeyFingerprint).HasMaxLength(128);
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

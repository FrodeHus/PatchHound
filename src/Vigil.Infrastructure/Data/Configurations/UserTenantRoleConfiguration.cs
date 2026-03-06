using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vigil.Core.Entities;

namespace Vigil.Infrastructure.Data.Configurations;

public class UserTenantRoleConfiguration : IEntityTypeConfiguration<UserTenantRole>
{
    public void Configure(EntityTypeBuilder<UserTenantRole> builder)
    {
        builder.HasKey(utr => utr.Id);

        builder.HasIndex(utr => new { utr.UserId, utr.TenantId, utr.Role }).IsUnique();

        builder.Property(utr => utr.Role).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(utr => utr.User)
            .WithMany(u => u.TenantRoles)
            .HasForeignKey(utr => utr.UserId);

        builder.HasOne(utr => utr.Tenant)
            .WithMany()
            .HasForeignKey(utr => utr.TenantId);
    }
}

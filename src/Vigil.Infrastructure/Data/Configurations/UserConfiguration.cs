using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vigil.Core.Entities;

namespace Vigil.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.HasIndex(u => u.EntraObjectId).IsUnique();
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(u => u.EntraObjectId).HasMaxLength(256).IsRequired();

        builder.HasMany(u => u.TenantRoles)
            .WithOne(utr => utr.User)
            .HasForeignKey(utr => utr.UserId);
    }
}

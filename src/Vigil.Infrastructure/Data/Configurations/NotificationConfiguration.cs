using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vigil.Core.Entities;

namespace Vigil.Infrastructure.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);

        builder.HasIndex(n => n.TenantId);
        builder.HasIndex(n => n.UserId);

        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.Title).HasMaxLength(512).IsRequired();
        builder.Property(n => n.Body).HasColumnType("text").IsRequired();
        builder.Property(n => n.RelatedEntityType).HasMaxLength(128);
    }
}

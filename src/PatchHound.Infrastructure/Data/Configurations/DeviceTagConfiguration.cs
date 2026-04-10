using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class DeviceTagConfiguration : IEntityTypeConfiguration<DeviceTag>
{
    public void Configure(EntityTypeBuilder<DeviceTag> builder)
    {
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => new { t.DeviceId, t.Key }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.Key });
        builder.HasIndex(t => t.TenantId);

        builder.Property(t => t.Key).HasMaxLength(DeviceTag.KeyMaxLength).IsRequired();
        builder.Property(t => t.Value).HasMaxLength(DeviceTag.ValueMaxLength).IsRequired();

        builder
            .HasOne<Device>()
            .WithMany()
            .HasForeignKey(t => t.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

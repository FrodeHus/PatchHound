using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class DeviceBusinessLabelConfiguration : IEntityTypeConfiguration<DeviceBusinessLabel>
{
    public void Configure(EntityTypeBuilder<DeviceBusinessLabel> builder)
    {
        builder.HasKey(item => new { item.DeviceId, item.BusinessLabelId, item.SourceKey });

        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => item.BusinessLabelId);
        builder.HasIndex(item => item.AssignedByRuleId);
        builder.HasIndex(item => new { item.DeviceId, item.BusinessLabelId }).IsUnique();

        builder.Property(item => item.SourceType).HasMaxLength(16);
        builder.Property(item => item.SourceKey).HasMaxLength(64);

        builder
            .HasOne<Device>()
            .WithMany()
            .HasForeignKey(item => item.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(item => item.BusinessLabel)
            .WithMany()
            .HasForeignKey(item => item.BusinessLabelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class InstalledSoftwareConfiguration : IEntityTypeConfiguration<InstalledSoftware>
{
    public void Configure(EntityTypeBuilder<InstalledSoftware> builder)
    {
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => new
        {
            i.TenantId,
            i.DeviceId,
            i.SoftwareProductId,
            i.SourceSystemId,
            i.Version
        }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.SoftwareProductId });
        builder.HasIndex(i => i.TenantId);
        builder.Property(i => i.Version).HasMaxLength(128).IsRequired();

        builder.HasOne<Device>().WithMany().HasForeignKey(i => i.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SoftwareProduct>().WithMany().HasForeignKey(i => i.SoftwareProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SourceSystem>().WithMany().HasForeignKey(i => i.SourceSystemId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantSoftwareProductInsightConfiguration : IEntityTypeConfiguration<TenantSoftwareProductInsight>
{
    public void Configure(EntityTypeBuilder<TenantSoftwareProductInsight> builder)
    {
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => new { i.TenantId, i.SoftwareProductId }).IsUnique();
        builder.Property(i => i.Description).HasMaxLength(4096);
        builder.Property(i => i.SupplyChainEvidenceJson).HasColumnType("text");

        builder.HasOne<SoftwareProduct>().WithMany().HasForeignKey(i => i.SoftwareProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanJobConfiguration : IEntityTypeConfiguration<ScanJob>
{
    public void Configure(EntityTypeBuilder<ScanJob> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ScanningToolVersionIdsJson).HasColumnType("text").IsRequired();
        b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.ErrorMessage).HasColumnType("text").IsRequired();
        b.HasIndex(x => new { x.ScanRunnerId, x.Status });
        b.HasIndex(x => x.RunId);
    }
}

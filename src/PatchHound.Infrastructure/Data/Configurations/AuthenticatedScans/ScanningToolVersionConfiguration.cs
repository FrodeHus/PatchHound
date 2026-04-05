using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanningToolVersionConfiguration : IEntityTypeConfiguration<ScanningToolVersion>
{
    public void Configure(EntityTypeBuilder<ScanningToolVersion> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ScriptContent).HasColumnType("text").IsRequired();
        b.HasIndex(x => new { x.ScanningToolId, x.VersionNumber }).IsUnique();
    }
}

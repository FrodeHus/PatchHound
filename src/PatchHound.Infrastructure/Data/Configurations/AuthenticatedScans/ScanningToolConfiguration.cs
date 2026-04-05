using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanningToolConfiguration : IEntityTypeConfiguration<ScanningTool>
{
    public void Configure(EntityTypeBuilder<ScanningTool> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Description).HasColumnType("text").IsRequired();
        b.Property(x => x.ScriptType).HasMaxLength(32).IsRequired();
        b.Property(x => x.InterpreterPath).HasMaxLength(512).IsRequired();
        b.Property(x => x.OutputModel).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

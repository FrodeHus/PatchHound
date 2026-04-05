using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanJobResultConfiguration : IEntityTypeConfiguration<ScanJobResult>
{
    public void Configure(EntityTypeBuilder<ScanJobResult> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.RawStdout).HasColumnType("text").IsRequired();
        b.Property(x => x.RawStderr).HasColumnType("text").IsRequired();
        b.Property(x => x.ParsedJson).HasColumnType("text").IsRequired();
        b.HasIndex(x => x.ScanJobId).IsUnique();
    }
}

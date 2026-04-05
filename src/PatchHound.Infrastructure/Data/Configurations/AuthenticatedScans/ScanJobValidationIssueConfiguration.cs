using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanJobValidationIssueConfiguration : IEntityTypeConfiguration<ScanJobValidationIssue>
{
    public void Configure(EntityTypeBuilder<ScanJobValidationIssue> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.FieldPath).HasMaxLength(256).IsRequired();
        b.Property(x => x.Message).HasColumnType("text").IsRequired();
        b.HasIndex(x => x.ScanJobId);
    }
}

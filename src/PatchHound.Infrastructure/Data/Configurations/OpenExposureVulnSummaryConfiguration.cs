using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Infrastructure.Data.Views;

namespace PatchHound.Infrastructure.Data.Configurations;

public class OpenExposureVulnSummaryConfiguration
    : IEntityTypeConfiguration<OpenExposureVulnSummary>
{
    public void Configure(EntityTypeBuilder<OpenExposureVulnSummary> builder)
    {
        builder.HasNoKey().ToView("mv_open_exposure_vuln_summary");
        builder.Property(x => x.VendorSeverity).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.MaxCvss).HasColumnType("numeric(5,2)");
    }
}

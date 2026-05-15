using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Infrastructure.Data.Views;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ExposureLatestAssessmentConfiguration
    : IEntityTypeConfiguration<ExposureLatestAssessment>
{
    public void Configure(EntityTypeBuilder<ExposureLatestAssessment> builder)
    {
        builder.HasNoKey().ToView("mv_exposure_latest_assessment");
        builder.Property(x => x.EnvironmentalCvss).HasColumnType("numeric(4,2)");
    }
}

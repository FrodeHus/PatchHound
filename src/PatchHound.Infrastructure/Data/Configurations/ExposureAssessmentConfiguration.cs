using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ExposureAssessmentConfiguration : IEntityTypeConfiguration<ExposureAssessment>
{
    public void Configure(EntityTypeBuilder<ExposureAssessment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.BaseCvss).HasColumnType("numeric(4,2)");
        builder.Property(a => a.EnvironmentalCvss).HasColumnType("numeric(4,2)");
        builder.Property(a => a.Reason).HasMaxLength(512);

        builder.HasOne(a => a.Exposure)
            .WithMany()
            .HasForeignKey(a => a.DeviceVulnerabilityExposureId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.SecurityProfile)
            .WithMany()
            .HasForeignKey(a => a.SecurityProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(a => a.Score);
        builder.HasIndex(a => new { a.TenantId, a.DeviceVulnerabilityExposureId }).IsUnique();
    }
}

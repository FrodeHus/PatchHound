using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ExposureAssessmentConfiguration : IEntityTypeConfiguration<ExposureAssessment>
{
    public void Configure(EntityTypeBuilder<ExposureAssessment> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.DeviceVulnerabilityExposureId).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.DeviceId });
        builder.HasIndex(x => new { x.TenantId, x.VulnerabilityId });

        builder.Property(x => x.Vector).HasMaxLength(512);
        builder.Property(x => x.ReasonSummary).HasMaxLength(2048);
        builder.Property(x => x.CalculationVersion).HasMaxLength(ExposureAssessment.CalculationVersionMaxLength).IsRequired();

        builder.HasOne(x => x.Exposure)
            .WithMany()
            .HasForeignKey(x => x.DeviceVulnerabilityExposureId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

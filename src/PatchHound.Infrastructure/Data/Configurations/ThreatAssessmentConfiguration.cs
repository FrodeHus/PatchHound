using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ThreatAssessmentConfiguration : IEntityTypeConfiguration<ThreatAssessment>
{
    public void Configure(EntityTypeBuilder<ThreatAssessment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ThreatScore).HasColumnType("numeric(4,2)");
        builder.Property(a => a.TechnicalScore).HasColumnType("numeric(4,2)");
        builder.Property(a => a.ExploitLikelihoodScore).HasColumnType("numeric(4,3)");
        builder.Property(a => a.ThreatActivityScore).HasColumnType("numeric(4,2)");
        builder.Property(a => a.EpssScore).HasColumnType("numeric(5,4)");
        builder.Property(a => a.FactorsJson).IsRequired();
        builder.Property(a => a.CalculationVersion).IsRequired().HasMaxLength(32);

        builder.HasOne(a => a.Vulnerability)
            .WithMany()
            .HasForeignKey(a => a.VulnerabilityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.VulnerabilityId).IsUnique();
    }
}

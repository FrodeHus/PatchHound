using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TeamRiskScoreConfiguration : IEntityTypeConfiguration<TeamRiskScore>
{
    public void Configure(EntityTypeBuilder<TeamRiskScore> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.TenantId);
        builder.HasIndex(item => item.TeamId).IsUnique();

        builder.Property(item => item.OverallScore).HasPrecision(7, 2);
        builder.Property(item => item.MaxAssetRiskScore).HasPrecision(7, 2);
        builder.Property(item => item.FactorsJson).HasColumnType("text").IsRequired();
        builder.Property(item => item.CalculationVersion).HasMaxLength(32).IsRequired();

        builder
            .HasOne(item => item.Team)
            .WithMany()
            .HasForeignKey(item => item.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

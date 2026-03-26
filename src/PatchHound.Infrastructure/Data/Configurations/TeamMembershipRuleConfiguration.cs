using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TeamMembershipRuleConfiguration : IEntityTypeConfiguration<TeamMembershipRule>
{
    public void Configure(EntityTypeBuilder<TeamMembershipRule> builder)
    {
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.FilterDefinition)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasIndex(rule => rule.TenantId);
        builder.HasIndex(rule => rule.TeamId)
            .IsUnique();

        builder.HasOne(rule => rule.Team)
            .WithOne()
            .HasForeignKey<TeamMembershipRule>(rule => rule.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class ApprovalTaskVisibleRoleConfiguration : IEntityTypeConfiguration<ApprovalTaskVisibleRole>
{
    public void Configure(EntityTypeBuilder<ApprovalTaskVisibleRole> builder)
    {
        builder.HasKey(role => role.Id);

        builder.HasIndex(role => new { role.ApprovalTaskId, role.Role }).IsUnique();
        builder.HasIndex(role => new { role.Role, role.ApprovalTaskId });

        builder.Property(role => role.Role).HasConversion<string>().HasMaxLength(64);

        builder
            .HasOne(role => role.ApprovalTask)
            .WithMany(task => task.VisibleRoles)
            .HasForeignKey(role => role.ApprovalTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

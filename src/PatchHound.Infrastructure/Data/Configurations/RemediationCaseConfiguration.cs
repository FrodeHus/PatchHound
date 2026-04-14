using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class RemediationCaseConfiguration : IEntityTypeConfiguration<RemediationCase>
{
    public void Configure(EntityTypeBuilder<RemediationCase> builder)
    {
        builder.ToTable("RemediationCases");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.SoftwareProductId).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.SoftwareProductId }).IsUnique();
        builder.HasIndex(c => c.TenantId);

        builder.HasOne(c => c.SoftwareProduct)
            .WithMany()
            .HasForeignKey(c => c.SoftwareProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

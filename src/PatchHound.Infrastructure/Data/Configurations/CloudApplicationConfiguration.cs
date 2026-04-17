using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class CloudApplicationConfiguration : IEntityTypeConfiguration<CloudApplication>
{
    public void Configure(EntityTypeBuilder<CloudApplication> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.TenantId, x.SourceSystemId, x.ExternalId }).IsUnique();
        builder.HasIndex(x => x.TenantId);

        builder.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Description).HasColumnType("text");

        builder.HasMany(x => x.Credentials)
            .WithOne(x => x.Application)
            .HasForeignKey(x => x.CloudApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(x => x.ActiveInTenant);

        builder.ToTable("CloudApplications");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class EnrichmentJobConfiguration : IEntityTypeConfiguration<EnrichmentJob>
{
    public void Configure(EntityTypeBuilder<EnrichmentJob> builder)
    {
        builder.HasKey(job => job.Id);

        builder
            .HasIndex(job => new
            {
                job.SourceKey,
                job.TargetModel,
                job.TargetId,
            })
            .IsUnique();
        builder.HasIndex(job => new
        {
            job.SourceKey,
            job.Status,
            job.NextAttemptAt,
        });
        builder.HasIndex(job => job.TenantId);

        builder.Property(job => job.SourceKey).HasMaxLength(128).IsRequired();
        builder.Property(job => job.TargetModel).HasConversion<string>().HasMaxLength(32);
        builder.Property(job => job.ExternalKey).HasMaxLength(256).IsRequired();
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(job => job.LeaseOwner).HasMaxLength(128);
        builder.Property(job => job.LastError).HasMaxLength(1024).IsRequired();
    }
}

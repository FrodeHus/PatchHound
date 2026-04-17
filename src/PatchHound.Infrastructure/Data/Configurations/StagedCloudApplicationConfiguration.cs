using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class StagedCloudApplicationConfiguration : IEntityTypeConfiguration<StagedCloudApplication>
{
    public void Configure(EntityTypeBuilder<StagedCloudApplication> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.IngestionRunId);
        builder.HasIndex(x => new { x.TenantId, x.SourceKey, x.ExternalId });

        builder.Property(x => x.SourceKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.PayloadJson).HasColumnType("text").IsRequired();

        builder.ToTable("StagedCloudApplications");
    }
}

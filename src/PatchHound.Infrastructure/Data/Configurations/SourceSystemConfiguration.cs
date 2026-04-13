using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SourceSystemConfiguration : IEntityTypeConfiguration<SourceSystem>
{
    public void Configure(EntityTypeBuilder<SourceSystem> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.Key).IsUnique();
        builder.Property(s => s.Key).HasMaxLength(64).IsRequired();
        builder.Property(s => s.DisplayName).HasMaxLength(256).IsRequired();
    }
}

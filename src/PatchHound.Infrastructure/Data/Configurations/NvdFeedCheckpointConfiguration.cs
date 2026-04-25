using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class NvdFeedCheckpointConfiguration : IEntityTypeConfiguration<NvdFeedCheckpoint>
{
    public void Configure(EntityTypeBuilder<NvdFeedCheckpoint> builder)
    {
        builder.HasKey(e => e.FeedName);
        builder.Property(e => e.FeedName).IsRequired().HasMaxLength(32);
    }
}

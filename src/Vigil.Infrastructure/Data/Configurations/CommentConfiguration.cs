using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vigil.Core.Entities;

namespace Vigil.Infrastructure.Data.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.HasKey(c => c.Id);

        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => new { c.EntityType, c.EntityId });

        builder.Property(c => c.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Content).HasColumnType("text").IsRequired();
    }
}

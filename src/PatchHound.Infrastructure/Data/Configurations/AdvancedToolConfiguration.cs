using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AdvancedToolConfiguration : IEntityTypeConfiguration<AdvancedTool>
{
    public void Configure(EntityTypeBuilder<AdvancedTool> builder)
    {
        builder.HasKey(tool => tool.Id);

        builder.Property(tool => tool.Name).HasMaxLength(256).IsRequired();
        builder.Property(tool => tool.Description).HasColumnType("text").IsRequired();
        builder.Property(tool => tool.SupportedAssetTypesJson).HasColumnType("text").IsRequired();
        builder.Property(tool => tool.KqlQuery).HasColumnType("text").IsRequired();
        builder.Property(tool => tool.AiPrompt).HasColumnType("text").IsRequired();

        builder.HasIndex(tool => tool.Name).IsUnique();
    }
}

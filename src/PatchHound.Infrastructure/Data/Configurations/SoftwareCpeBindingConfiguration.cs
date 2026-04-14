using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SoftwareCpeBindingConfiguration : IEntityTypeConfiguration<SoftwareCpeBinding>
{
    public void Configure(EntityTypeBuilder<SoftwareCpeBinding> builder)
    {
        builder.HasKey(binding => binding.Id);

        builder.HasIndex(binding => binding.SoftwareProductId).IsUnique();

        builder.Property(binding => binding.Cpe23Uri).HasMaxLength(2048).IsRequired();
        builder.Property(binding => binding.BindingMethod).HasConversion<string>().HasMaxLength(32);
        builder.Property(binding => binding.Confidence).HasConversion<string>().HasMaxLength(16);
        builder.Property(binding => binding.MatchedVendor).HasMaxLength(256);
        builder.Property(binding => binding.MatchedProduct).HasMaxLength(256);
        builder.Property(binding => binding.MatchedVersion).HasMaxLength(256);

        builder
            .HasOne(binding => binding.SoftwareProduct)
            .WithMany()
            .HasForeignKey(binding => binding.SoftwareProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

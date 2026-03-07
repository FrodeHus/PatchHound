using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class TenantSlaConfigurationConfiguration : IEntityTypeConfiguration<TenantSlaConfiguration>
{
    public void Configure(EntityTypeBuilder<TenantSlaConfiguration> builder)
    {
        builder.HasKey(config => config.TenantId);
    }
}

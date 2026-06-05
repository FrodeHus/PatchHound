using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.Ingestion;

namespace PatchHound.Infrastructure.Data.Configurations.Ingestion;

public sealed class IngestionRunDeltaConfiguration : IEntityTypeConfiguration<IngestionRunDelta>
{
    public void Configure(EntityTypeBuilder<IngestionRunDelta> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.RunId, x.Kind, x.EntityId })
            .IsUnique()
            .HasDatabaseName("UX_IngestionRunDeltas_Run_Kind_Id");
        builder.HasIndex(x => new { x.TenantId, x.Kind });
        builder.Property(x => x.Kind).HasMaxLength(64).IsRequired();
        builder.HasOne<IngestionRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

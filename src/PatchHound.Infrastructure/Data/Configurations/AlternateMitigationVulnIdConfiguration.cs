using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Infrastructure.Data.Views;

namespace PatchHound.Infrastructure.Data.Configurations;

public class AlternateMitigationVulnIdConfiguration
    : IEntityTypeConfiguration<AlternateMitigationVulnId>
{
    public void Configure(EntityTypeBuilder<AlternateMitigationVulnId> builder)
    {
        builder.HasNoKey().ToView("mv_alternate_mitigation_vuln_ids");
    }
}

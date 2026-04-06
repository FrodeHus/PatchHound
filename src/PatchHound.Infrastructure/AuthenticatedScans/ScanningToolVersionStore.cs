using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class ScanningToolVersionStore(PatchHoundDbContext db)
{
    private const int MaxRetainedVersions = 10;

    public async Task<ScanningToolVersion> PublishNewVersionAsync(
        Guid scanningToolId, string scriptContent, Guid editedByUserId, CancellationToken ct)
    {
        var tool = await db.ScanningTools.SingleAsync(t => t.Id == scanningToolId, ct);

        var maxVersion = await db.ScanningToolVersions
            .Where(v => v.ScanningToolId == scanningToolId)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

        var version = ScanningToolVersion.Create(scanningToolId, maxVersion + 1, scriptContent, editedByUserId);
        db.ScanningToolVersions.Add(version);
        tool.SetCurrentVersion(version.Id);
        await db.SaveChangesAsync(ct);

        // Prune old versions beyond retention window
        var staleVersions = await db.ScanningToolVersions
            .Where(v => v.ScanningToolId == scanningToolId)
            .OrderByDescending(v => v.VersionNumber)
            .Skip(MaxRetainedVersions)
            .ToListAsync(ct);

        if (staleVersions.Count > 0)
        {
            db.ScanningToolVersions.RemoveRange(staleVersions);
            await db.SaveChangesAsync(ct);
        }

        return version;
    }
}

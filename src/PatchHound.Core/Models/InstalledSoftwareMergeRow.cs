namespace PatchHound.Core.Models;

public sealed record InstalledSoftwareMergeRow(
    Guid TenantId,
    Guid DeviceId,
    Guid SoftwareProductId,
    Guid SourceSystemId,
    string Version,
    DateTimeOffset ObservedAt,
    Guid RunId);

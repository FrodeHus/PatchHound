namespace PatchHound.Core.Models;

public sealed record DeviceMergeRow(
    Guid TenantId,
    Guid SourceSystemId,
    string ExternalId,
    string Name,
    string? ComputerDnsName,
    string? HealthStatus,
    string? OsPlatform,
    string? OsVersion,
    string? ExternalRiskLabel,
    DateTimeOffset? LastSeenAt,
    string? LastIpAddress,
    string? AadDeviceId,
    string? GroupId,
    string? GroupName,
    string? ExposureLevel,
    bool? IsAadJoined,
    string? OnboardingStatus,
    string? DeviceValue,
    bool IsActive);

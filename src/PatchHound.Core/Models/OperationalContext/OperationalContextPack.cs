using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatchHound.Core.Models.OperationalContext;

public sealed class OperationalContextPack
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ContextKind { get; set; } = string.Empty;
    public SubjectInfo Subject { get; set; } = new();
    public ScopeInfo Scope { get; set; } = new();
    public RiskInfo Risk { get; set; } = new();
    public WorkflowInfo? Workflow { get; set; }
    public List<OperationalContextCitation> Citations { get; set; } = [];
    public LimitsInfo Limits { get; set; } = new();

    public sealed class SubjectInfo
    {
        public Guid? RemediationCaseId { get; set; }
        public Guid? VulnerabilityId { get; set; }
        public SoftwareProductInfo? SoftwareProduct { get; set; }
        public string? VulnerabilityExternalId { get; set; }
    }

    public sealed class SoftwareProductInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Vendor { get; set; }
    }

    public sealed class ScopeInfo
    {
        public int OpenExposureCount { get; set; }
        public int AffectedDeviceCount { get; set; }
        public Dictionary<string, int> CriticalityDistribution { get; set; } = [];
        public int InternetExposureCount { get; set; }
        public List<LabelCount> TopBusinessLabels { get; set; } = [];
        public List<NamedCount> TopSecurityProfiles { get; set; } = [];
        public List<NamedCount> TopOwnerTeams { get; set; } = [];
    }

    public sealed class RiskInfo
    {
        public decimal? SoftwareRiskScore { get; set; }
        public decimal? MaxDeviceRiskScore { get; set; }
        public string? HighestVendorSeverity { get; set; }
    }

    public sealed class WorkflowInfo
    {
        public string? Status { get; set; }
        public string? CurrentStage { get; set; }
        public int PendingApprovalCount { get; set; }
        public int ActivePatchingTaskCount { get; set; }
    }

    public sealed class LimitsInfo
    {
        public int TopDeviceLimit { get; set; }
        public int ExposureLimit { get; set; }
        public bool Truncated { get; set; }
    }

    public sealed class LabelCount
    {
        public string Name { get; set; } = string.Empty;
        public string? WeightCategory { get; set; }
        public int DeviceCount { get; set; }
    }

    public sealed class NamedCount
    {
        public string Name { get; set; } = string.Empty;
        public int DeviceCount { get; set; }
    }
}

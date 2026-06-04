namespace PatchHound.Api.Services;

public static class DeviceRuleAssetTypes
{
    public const string Device = "Device";
    public const string Software = "Software";
    public const string Application = "Application";

    public static bool IsSupported(string assetType) =>
        string.Equals(assetType, Device, StringComparison.OrdinalIgnoreCase)
        || string.Equals(assetType, Software, StringComparison.OrdinalIgnoreCase)
        || string.Equals(assetType, Application, StringComparison.OrdinalIgnoreCase);

    public static bool IsDevice(string assetType) =>
        string.Equals(assetType, Device, StringComparison.OrdinalIgnoreCase);

    public static bool IsSoftware(string assetType) =>
        string.Equals(assetType, Software, StringComparison.OrdinalIgnoreCase);

    public static bool IsApplication(string assetType) =>
        string.Equals(assetType, Application, StringComparison.OrdinalIgnoreCase);
}

public static class DeviceRuleOperationTypes
{
    public const string AssignSecurityProfile = "AssignSecurityProfile";
    public const string AssignTeam = "AssignTeam";
    public const string AssignOwnerTeam = "AssignOwnerTeam";
    public const string AssignBusinessLabel = "AssignBusinessLabel";
    public const string SetCriticality = "SetCriticality";
}

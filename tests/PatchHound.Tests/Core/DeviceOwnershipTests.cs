using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using Xunit;

namespace PatchHound.Tests.Core;

public class DeviceOwnershipTests
{
    // --- DeviceBusinessLabel ---

    [Fact]
    public void DeviceBusinessLabel_Create_sets_ids()
    {
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var labelId = Guid.NewGuid();
        var link = DeviceBusinessLabel.Create(tenantId, deviceId, labelId);
        Assert.Equal(tenantId, link.TenantId);
        Assert.Equal(deviceId, link.DeviceId);
        Assert.Equal(labelId, link.BusinessLabelId);
        Assert.Equal(DeviceBusinessLabel.ManualSourceType, link.SourceType);
    }

    [Fact]
    public void DeviceBusinessLabel_CreateManual_rejects_empty_tenantId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceBusinessLabel.CreateManual(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), assignedBy: null));
        Assert.Equal("tenantId", ex.ParamName);
    }

    [Fact]
    public void DeviceBusinessLabel_CreateManual_rejects_empty_deviceId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceBusinessLabel.CreateManual(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), assignedBy: null));
        Assert.Equal("deviceId", ex.ParamName);
    }

    [Fact]
    public void DeviceBusinessLabel_CreateRule_rejects_empty_ruleId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceBusinessLabel.CreateRule(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));
        Assert.Equal("ruleId", ex.ParamName);
    }

    // --- DeviceTag ---

    [Fact]
    public void DeviceTag_Create_sets_kv_pair()
    {
        var tag = DeviceTag.Create(Guid.NewGuid(), Guid.NewGuid(), "env", "prod");
        Assert.Equal("env", tag.Key);
        Assert.Equal("prod", tag.Value);
    }

    [Fact]
    public void DeviceTag_Create_trims_key_and_value()
    {
        var tag = DeviceTag.Create(Guid.NewGuid(), Guid.NewGuid(), "  env  ", "  prod  ");
        Assert.Equal("env", tag.Key);
        Assert.Equal("prod", tag.Value);
    }

    [Fact]
    public void DeviceTag_Create_rejects_empty_tenantId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceTag.Create(Guid.Empty, Guid.NewGuid(), "env", "prod"));
        Assert.Equal("tenantId", ex.ParamName);
    }

    [Fact]
    public void DeviceTag_Create_rejects_empty_deviceId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceTag.Create(Guid.NewGuid(), Guid.Empty, "env", "prod"));
        Assert.Equal("deviceId", ex.ParamName);
    }

    [Fact]
    public void DeviceTag_Create_rejects_whitespace_key()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceTag.Create(Guid.NewGuid(), Guid.NewGuid(), "   ", "prod"));
        Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    public void DeviceTag_Create_rejects_key_longer_than_max()
    {
        var longKey = new string('a', DeviceTag.KeyMaxLength + 1);
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceTag.Create(Guid.NewGuid(), Guid.NewGuid(), longKey, "prod"));
        Assert.Equal("key", ex.ParamName);
    }

    // --- DeviceRule ---

    private static FilterNode SampleFilter() =>
        new FilterGroup("AND", new List<FilterNode>());

    private static List<AssetRuleOperation> SampleOperations() =>
        new() { new AssetRuleOperation("noop", new Dictionary<string, string>()) };

    [Fact]
    public void DeviceRule_Create_sets_fields_and_enables_rule()
    {
        var tenantId = Guid.NewGuid();
        var rule = DeviceRule.Create(tenantId, "My Rule", "desc", 5, SampleFilter(), SampleOperations());
        Assert.Equal(tenantId, rule.TenantId);
        Assert.Equal("My Rule", rule.Name);
        Assert.Equal("desc", rule.Description);
        Assert.Equal(5, rule.Priority);
        Assert.True(rule.Enabled);
    }

    [Fact]
    public void DeviceRule_Create_rejects_empty_tenantId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceRule.Create(Guid.Empty, "name", null, 1, SampleFilter(), SampleOperations()));
        Assert.Equal("tenantId", ex.ParamName);
    }

    [Fact]
    public void DeviceRule_Create_rejects_whitespace_name()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceRule.Create(Guid.NewGuid(), "   ", null, 1, SampleFilter(), SampleOperations()));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void DeviceRule_Create_rejects_name_longer_than_max()
    {
        var longName = new string('a', DeviceRule.NameMaxLength + 1);
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceRule.Create(Guid.NewGuid(), longName, null, 1, SampleFilter(), SampleOperations()));
        Assert.Equal("name", ex.ParamName);
    }

    // --- DeviceRiskScore ---

    [Fact]
    public void DeviceRiskScore_Create_sets_ids_and_scores()
    {
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var score = DeviceRiskScore.Create(
            tenantId,
            deviceId,
            overallScore: 42.5m,
            maxEpisodeRiskScore: 80m,
            criticalCount: 1,
            highCount: 2,
            mediumCount: 3,
            lowCount: 4,
            openEpisodeCount: 5,
            factorsJson: "[]",
            calculationVersion: "v1");
        Assert.Equal(tenantId, score.TenantId);
        Assert.Equal(deviceId, score.DeviceId);
        Assert.Equal(42.5m, score.OverallScore);
        Assert.Equal("v1", score.CalculationVersion);
    }

    [Fact]
    public void DeviceRiskScore_Create_rejects_empty_tenantId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceRiskScore.Create(
                Guid.Empty, Guid.NewGuid(), 0m, 0m, 0, 0, 0, 0, 0, "[]", "v1"));
        Assert.Equal("tenantId", ex.ParamName);
    }

    [Fact]
    public void DeviceRiskScore_Create_rejects_empty_deviceId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceRiskScore.Create(
                Guid.NewGuid(), Guid.Empty, 0m, 0m, 0, 0, 0, 0, 0, "[]", "v1"));
        Assert.Equal("deviceId", ex.ParamName);
    }

    [Fact]
    public void DeviceRiskScore_Create_rejects_calculationVersion_longer_than_max()
    {
        var longVersion = new string('v', DeviceRiskScore.CalculationVersionMaxLength + 1);
        var ex = Assert.Throws<ArgumentException>(() =>
            DeviceRiskScore.Create(
                Guid.NewGuid(), Guid.NewGuid(), 0m, 0m, 0, 0, 0, 0, 0, "[]", longVersion));
        Assert.Equal("calculationVersion", ex.ParamName);
    }

    // --- SecurityProfile ---

    [Fact]
    public void SecurityProfile_Create_initializes_modifiers_to_zero()
    {
        var profile = SecurityProfile.Create(Guid.NewGuid(), "Gold", description: null);
        Assert.Equal("Gold", profile.Name);
        Assert.Equal(CvssModifiedAttackComplexity.NotDefined, profile.ModifiedAttackComplexity);
        Assert.Equal(CvssModifiedPrivilegesRequired.NotDefined, profile.ModifiedPrivilegesRequired);
        Assert.Equal(CvssModifiedImpact.NotDefined, profile.ModifiedConfidentialityImpact);
        Assert.Equal(CvssModifiedImpact.NotDefined, profile.ModifiedIntegrityImpact);
        Assert.Equal(CvssModifiedImpact.NotDefined, profile.ModifiedAvailabilityImpact);
    }

    [Fact]
    public void SecurityProfile_Create_rejects_empty_tenantId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SecurityProfile.Create(Guid.Empty, "Gold", description: null));
        Assert.Equal("tenantId", ex.ParamName);
    }

    [Fact]
    public void SecurityProfile_Create_rejects_whitespace_name()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SecurityProfile.Create(Guid.NewGuid(), "   ", description: null));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void SecurityProfile_Create_rejects_name_longer_than_max()
    {
        var longName = new string('n', SecurityProfile.NameMaxLength + 1);
        var ex = Assert.Throws<ArgumentException>(() =>
            SecurityProfile.Create(Guid.NewGuid(), longName, description: null));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void SecurityProfile_Create_trims_name_and_description()
    {
        var profile = SecurityProfile.Create(Guid.NewGuid(), "  Gold  ", "  desc  ");
        Assert.Equal("Gold", profile.Name);
        Assert.Equal("desc", profile.Description);
    }
}

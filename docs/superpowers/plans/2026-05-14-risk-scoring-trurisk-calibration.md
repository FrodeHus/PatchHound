# TruRisk-Inspired Risk Scoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recalibrate PatchHound risk scoring so dashboard, device, software, team, tenant, and remediation-case scores use a consistent 0-1000 model that reflects vulnerability likelihood, business impact, exposure breadth, and emergency urgency.

**Architecture:** Extract pure scoring formulas from `RiskScoreService` into a focused core scoring model, then have `RiskScoreService` feed it enriched exposure inputs. Preserve existing persisted score entities and API DTOs in the first pass, but update `CalculationVersion` and `FactorsJson` so every UI can explain why scores changed. Use Qualys TruRisk as inspiration, not a literal clone: PatchHound will use available `ExposureAssessment`, `ThreatAssessment`, `VulnerabilityPatchAssessment`, device `Criticality`, and business-label weights.

**Tech Stack:** C#/.NET, EF Core, xUnit, FluentAssertions, NSubstitute, PatchHound existing test-data builders.

---

## Background And Risk

GitNexus impact analysis reports this as high-risk shared behavior:

- `RiskScoreService`: CRITICAL, 107 direct dependents, 279 total impacted symbols.
- `CalculateTenantRisk`: CRITICAL, affects dashboard summary, risk-score API, refresh flows, ingestion snapshots.
- `CalculateSoftwareScoresAsync`: CRITICAL, affects remediation cases, software views, dashboards, refresh flows.
- `BuildAssetRiskResults`: HIGH, affects device, group, team, tenant rollups.

Current root cause:

- Device/software formulas mix CVSS-scale values (`0-10`) with a `0-1000` display band (`500/750/900`).
- High exposure count caps are too low (`60` for software, `60` for device), so 49 high findings barely move a score.
- `AffectedDeviceCount` is not part of `SoftwareRiskScore.OverallScore`.
- Emergency patch recommendations and threat-intel flags are not part of the displayed risk score.
- Dashboard overall risk is derived from the same under-scaled device scores, so it also appears low.

Qualys model references:

- TruRisk combines asset criticality, detection/vulnerability risk scores, severity weighting, count weighting, and external exposure into a capped `0-1000` score.
- QDS/QVS are `1-100` likelihood-style scores based on CVSS plus threat intelligence such as EPSS, CISA KEV, exploit maturity, malware/ransomware, active exploitation, and trending indicators.
- Newer TruRisk uses a max detection score and count bonuses, with external assets weighted higher.

---

## Target Model

Use one internal detection score per open exposure:

```csharp
DetectionScore = max(
    EnvironmentalCvss * 10,
    ThreatAssessment.ThreatScore,
    urgencyFloor,
    threatIntelFloor)
```

Floors:

```csharp
Emergency patch recommended: 95
Known exploited, active alert, ransomware, malware: 95
Public exploit: 80
EPSS >= 0.50: 80
Vendor severity Critical with no threat record: 90
Vendor severity High with no threat record: 70
```

Asset criticality multiplier:

```csharp
Critical = 1.35m
High = 1.20m
Medium = 1.00m
Low = 0.85m
```

Business label multiplier remains as today:

```csharp
Critical label = 2.0m
Sensitive label = 1.5m
Normal label = 1.0m
Informational label = 0.5m
```

Risk bands should be centralized:

```csharp
Critical >= 850
High     >= 700
Medium   >= 500
Low      >  0
None     == 0
```

This aligns with Qualys-style bands and avoids today’s mismatch where `124` appears Low despite critical/high findings.

---

## Files

- Create: `src/PatchHound.Core/Services/RiskScoring/RiskBand.cs`
- Create: `src/PatchHound.Core/Services/RiskScoring/RiskScoringModels.cs`
- Create: `src/PatchHound.Core/Services/RiskScoring/PatchHoundRiskScoringEngine.cs`
- Modify: `src/PatchHound.Infrastructure/Services/RiskScoreService.cs`
- Modify: `src/PatchHound.Api/Services/ApprovalTaskQueryService.cs`
- Modify: `src/PatchHound.Api/Services/RemediationDecisionQueryService.cs`
- Modify: `src/PatchHound.Api/Services/DeviceDetailQueryService.cs`
- Modify: `src/PatchHound.Api/Controllers/DashboardController.cs`
- Modify: frontend risk-band helpers only if text/colors assume old `900/750/500` thresholds.
- Test: `tests/PatchHound.Tests/Core/RiskScoring/PatchHoundRiskScoringEngineTests.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/Services/RiskScoreServiceTruRiskCalibrationTests.cs`
- Update existing tests that assert exact old scores.

---

### Task 1: Add Central Risk Bands

**Files:**
- Create: `src/PatchHound.Core/Services/RiskScoring/RiskBand.cs`
- Test: `tests/PatchHound.Tests/Core/RiskScoring/PatchHoundRiskScoringEngineTests.cs`

- [ ] **Step 1: Write failing band tests**

```csharp
[Theory]
[InlineData(0, "None")]
[InlineData(1, "Low")]
[InlineData(499.99, "Low")]
[InlineData(500, "Medium")]
[InlineData(700, "High")]
[InlineData(850, "Critical")]
public void RiskBand_FromScore_UsesUnifiedTruRiskInspiredThresholds(decimal score, string expected)
{
    RiskBand.FromScore(score).Should().Be(expected);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests\PatchHound.Tests\PatchHound.Tests.csproj --filter "FullyQualifiedName~PatchHoundRiskScoringEngineTests" -v minimal`

Expected: fails because `RiskBand` does not exist.

- [ ] **Step 3: Implement `RiskBand`**

```csharp
namespace PatchHound.Core.Services.RiskScoring;

public static class RiskBand
{
    public const decimal MediumThreshold = 500m;
    public const decimal HighThreshold = 700m;
    public const decimal CriticalThreshold = 850m;

    public static string FromScore(decimal score) => score switch
    {
        >= CriticalThreshold => "Critical",
        >= HighThreshold => "High",
        >= MediumThreshold => "Medium",
        > 0m => "Low",
        _ => "None",
    };
}
```

- [ ] **Step 4: Run the band tests**

Expected: pass.

---

### Task 2: Add Pure Detection And Score Model

**Files:**
- Create: `src/PatchHound.Core/Services/RiskScoring/RiskScoringModels.cs`
- Create: `src/PatchHound.Core/Services/RiskScoring/PatchHoundRiskScoringEngine.cs`
- Test: `tests/PatchHound.Tests/Core/RiskScoring/PatchHoundRiskScoringEngineTests.cs`

- [ ] **Step 1: Add failing tests for emergency and high-volume cases**

```csharp
[Fact]
public void CalculateSoftwareRisk_EmergencyCriticalAndManyHighs_IsAtLeastHigh()
{
    var inputs = new List<RiskExposureInput>
    {
        RiskExposureInput.Critical(Guid.NewGuid(), Guid.NewGuid(), 9.8m, isEmergency: true),
    };
    inputs.AddRange(Enumerable.Range(0, 49)
        .Select(_ => RiskExposureInput.High(Guid.NewGuid(), Guid.NewGuid(), 7.5m)));

    var result = PatchHoundRiskScoringEngine.CalculateSoftwareRisk(inputs, affectedDeviceCount: 1, highValueDeviceCount: 0);

    result.OverallScore.Should().BeGreaterThanOrEqualTo(700m);
    result.RiskBand.Should().Be("High");
}

[Fact]
public void CalculateAssetRisk_SingleCriticalOnHighCriticalityAsset_IsAtLeastMedium()
{
    var input = RiskExposureInput.Critical(Guid.NewGuid(), Guid.NewGuid(), 9.5m, assetCriticality: Criticality.High);

    var result = PatchHoundRiskScoringEngine.CalculateAssetRisk([input], businessLabelWeight: 1m);

    result.OverallScore.Should().BeGreaterThanOrEqualTo(500m);
}
```

- [ ] **Step 2: Implement model records**

```csharp
namespace PatchHound.Core.Services.RiskScoring;

public sealed record RiskExposureInput(
    Guid DeviceId,
    Guid VulnerabilityId,
    decimal EnvironmentalCvss,
    Severity VendorSeverity,
    Criticality AssetCriticality,
    decimal? ThreatScore,
    decimal? EpssScore,
    bool KnownExploited,
    bool PublicExploit,
    bool ActiveAlert,
    bool HasRansomwareAssociation,
    bool HasMalwareAssociation,
    bool IsEmergencyPatchRecommended
);

public sealed record RiskScoreResult(
    decimal OverallScore,
    decimal MaxDetectionScore,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    string RiskBand,
    string FactorsJson
);
```

- [ ] **Step 3: Implement scoring engine**

Use:

```csharp
baseComponent = maxDetectionScore * 6.5m * severityGate;
countComponent = criticalCount * 45m + highCount * 12m + mediumCount * 3m + lowCount * 1m;
breadthComponent = affectedDeviceCount <= 1 ? 0m : Math.Min((decimal)Math.Log10(affectedDeviceCount) * 80m, 180m);
highValueComponent = highValueDeviceCount * 25m;
score = Math.Clamp(Math.Round((baseComponent + countComponent + breadthComponent + highValueComponent) * impactMultiplier, 2), 0m, 1000m);
```

Apply floors after the numeric score:

```csharp
if (inputs.Any(i => i.IsEmergencyPatchRecommended || i.KnownExploited || i.ActiveAlert || i.HasRansomwareAssociation))
    score = Math.Max(score, 700m);
if (inputs.Any(i => i.VendorSeverity == Severity.Critical))
    score = Math.Max(score, 500m);
if (inputs.Count(i => i.VendorSeverity == Severity.High) >= 10)
    score = Math.Max(score, 500m);
```

- [ ] **Step 4: Run pure scoring tests**

Expected: pass.

---

### Task 3: Feed Enriched Inputs From `RiskScoreService`

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/RiskScoreService.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/Services/RiskScoreServiceTruRiskCalibrationTests.cs`

- [ ] **Step 1: Run impact check before editing**

Run GitNexus: `impact({ repo: "PatchHound", target: "RiskScoreService", direction: "upstream" })`

Expected: CRITICAL. Proceed because this plan explicitly covers dependent score surfaces and tests.

- [ ] **Step 2: Add failing integration tests**

Test cases:

```csharp
RecalculateForTenantAsync_EmergencyCriticalSoftwareCase_ScoresAtLeastHigh
RecalculateForTenantAsync_DashboardTenantRisk_IncreasesWhenAssetScoresIncrease
RecalculateForTenantAsync_AffectedDeviceBreadth_IncreasesSoftwareScore
RecalculateForTenantAsync_KnownExploitedThreatAssessment_AppliesHighFloor
```

The emergency test must seed:

- one software product
- one affected device
- one critical vulnerability with `VulnerabilityPatchAssessment.UrgencyTier = "emergency"`
- forty-nine high vulnerabilities on the same software product/device

Expected:

```csharp
score.OverallScore.Should().BeGreaterThanOrEqualTo(700m);
RiskBand.FromScore(score.OverallScore).Should().Be("High");
```

- [ ] **Step 3: Replace private formula bodies with engine calls**

In `CalculateAssetScoresAsync`, select device criticality, threat assessment fields, and patch assessment urgency into the exposure rows. Convert each open exposure into `RiskExposureInput`, call `PatchHoundRiskScoringEngine.CalculateAssetRisk`, then persist its fields.

In `CalculateSoftwareScoresAsync`, include the same enriched inputs, group by software product, call `PatchHoundRiskScoringEngine.CalculateSoftwareRisk`, and persist:

- `OverallScore = result.OverallScore`
- `MaxExposureScore = result.MaxDetectionScore`
- count fields from result
- `FactorsJson = result.FactorsJson`

Set:

```csharp
public const string CalculationVersion = "2-trurisk-inspired";
```

- [ ] **Step 4: Keep remediation adjustment behavior**

For approved patching inside a valid maintenance window, reduce `DetectionScore` by `RemediationAdjustmentFactor`, not just raw CVSS. Do not reduce risk acceptance. Continue excluding approved alternate mitigations.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\PatchHound.Tests\PatchHound.Tests.csproj --filter "FullyQualifiedName~RiskScoreServiceTruRiskCalibrationTests|FullyQualifiedName~RiskScoreServiceCanonicalTests|FullyQualifiedName~RiskScoreRemediationTests|FullyQualifiedName~BusinessLabelRiskWeightTests" -v minimal
```

Expected: pass.

---

### Task 4: Centralize API Risk Band Resolution

**Files:**
- Modify: `src/PatchHound.Api/Services/ApprovalTaskQueryService.cs`
- Modify: `src/PatchHound.Api/Services/RemediationDecisionQueryService.cs`
- Modify: `src/PatchHound.Api/Services/DeviceDetailQueryService.cs`
- Modify: `src/PatchHound.Api/Controllers/DashboardController.cs`
- Test: relevant API tests already asserting risk bands.

- [ ] **Step 1: Replace duplicated thresholds**

Replace local `ResolveRiskBand` or switch expressions using `900/750/500` with:

```csharp
RiskBand.FromScore(score)
```

For dashboard executive wording, map `Medium` to existing UI term only if product wants to preserve `"Elevated"`:

```csharp
private static string DescribeRiskLevel(decimal score) =>
    RiskBand.FromScore(score) == "Medium" ? "Elevated" : RiskBand.FromScore(score);
```

- [ ] **Step 2: Run API tests**

Run:

```powershell
dotnet test tests\PatchHound.Tests\PatchHound.Tests.csproj --filter "FullyQualifiedName~DashboardControllerExecutiveSummaryTests|FullyQualifiedName~DevicesControllerTests|FullyQualifiedName~RemediationDecisionListTests|FullyQualifiedName~TeamsControllerTests" -v minimal
```

Expected: pass after updating exact-score assertions to band/relative assertions where needed.

---

### Task 5: Update Frontend Threshold Helpers

**Files:**
- Modify frontend components found by `rg -n "score >= 900|score >= 750|score >= 500" frontend/src`

- [ ] **Step 1: Replace hardcoded thresholds with shared helper**

Create or update a frontend helper:

```ts
export function riskBand(score: number | null | undefined) {
  if (!score || score <= 0) return 'none'
  if (score >= 850) return 'critical'
  if (score >= 700) return 'high'
  if (score >= 500) return 'medium'
  return 'low'
}
```

- [ ] **Step 2: Update affected components**

Replace local threshold logic in dashboard, devices, and software components with the helper.

- [ ] **Step 3: Run frontend checks**

Run:

```powershell
cd frontend
npm run lint
npm run typecheck
npm test
```

Expected: pass.

---

### Task 6: Verification And Migration Notes

**Files:**
- Modify: `docs/testing-conventions.md` only if adding scoring-test guidance is desired.

- [ ] **Step 1: Run backend focused suite**

Run:

```powershell
dotnet test tests\PatchHound.Tests\PatchHound.Tests.csproj --filter "FullyQualifiedName~RiskScoreService|FullyQualifiedName~RiskScoreController|FullyQualifiedName~DashboardController|FullyQualifiedName~RemediationDecision|FullyQualifiedName~ApprovalTask" -v minimal
```

Expected: pass.

- [ ] **Step 2: Run build**

Run:

```powershell
dotnet build PatchHound.slnx
```

Expected: pass.

- [ ] **Step 3: Run GitNexus change detection before commit**

Run:

```text
detect_changes({ repo: "PatchHound", scope: "all" })
```

Expected: affected symbols are limited to scoring engine, `RiskScoreService`, risk-band consumers, and tests.

- [ ] **Step 4: Operational recalculation**

After deploy, run tenant risk recalculation for existing tenants because persisted `DeviceRiskScores`, `SoftwareRiskScores`, `DeviceGroupRiskScores`, `TeamRiskScores`, and `TenantRiskScoreSnapshots` contain old-version values. Historical snapshots should either keep their old values with `CalculationVersion = "1"` context, or be backfilled if product wants trend continuity.

---

## Acceptance Criteria

- A remediation case with 1 critical emergency vulnerability and 49 high vulnerabilities no longer appears as Low.
- Dashboard overall risk increases proportionally when high-risk device/software scores increase.
- One affected device can still reduce fleet-wide exposure, but it cannot hide emergency urgency.
- Scores remain capped at `1000`.
- Business-label weighting still works.
- Approved patching still reduces active risk during a valid maintenance window.
- Risk acceptance remains visibility-only and does not reduce score.
- Alternate mitigation still removes covered vulnerabilities from active risk.
- All score band labels use centralized thresholds.

using System.Text.Json;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Infrastructure.Services;

public class EnvironmentalSeverityCalculator
{
    public const string CalculationVersion = "1";

    public EnvironmentalSeverityCalculationResult Calculate(
        Vulnerability vulnerability,
        Asset asset,
        AssetSecurityProfile? profile
    )
    {
        if (vulnerability.CvssScore is null || string.IsNullOrWhiteSpace(vulnerability.CvssVector))
        {
            return EnvironmentalSeverityCalculationResult.FromBaseSeverity(
                vulnerability.VendorSeverity,
                vulnerability.CvssScore,
                vulnerability.CvssVector,
                profile?.Id,
                "No CVSS vector is available; effective severity matches vendor severity."
            );
        }

        var parsed = CvssVector.TryParse(vulnerability.CvssVector!);
        if (parsed is null)
        {
            return EnvironmentalSeverityCalculationResult.FromBaseSeverity(
                vulnerability.VendorSeverity,
                vulnerability.CvssScore,
                vulnerability.CvssVector,
                profile?.Id,
                "CVSS vector could not be parsed; effective severity matches vendor severity."
            );
        }

        var factors = new List<AssessmentFactor>
        {
            new(
                "BaseSeverity",
                vulnerability.VendorSeverity.ToString(),
                "Vendor/base severity from the source feed."
            ),
            new("AssetCriticality", asset.Criticality.ToString(), "Current asset criticality."),
        };

        if (profile is null)
        {
            return EnvironmentalSeverityCalculationResult.FromBaseSeverity(
                vulnerability.VendorSeverity,
                vulnerability.CvssScore,
                vulnerability.CvssVector,
                null,
                "No security profile is assigned; effective severity matches vendor severity.",
                factors
            );
        }

        var modifiedAttackVector = ResolveAttackVector(parsed.AttackVector, profile.ModifiedAttackVector);
        var modifiedAttackComplexity = ResolveAttackComplexity(
            parsed.AttackComplexity,
            profile.ModifiedAttackComplexity
        );
        var modifiedPrivilegesRequired = ResolvePrivilegesRequired(
            parsed.PrivilegesRequired,
            profile.ModifiedPrivilegesRequired
        );
        var modifiedUserInteraction = ResolveUserInteraction(
            parsed.UserInteraction,
            profile.ModifiedUserInteraction
        );
        var modifiedScope = ResolveScope(parsed.Scope, profile.ModifiedScope);
        var modifiedConfidentialityImpact = ResolveImpact(
            parsed.ConfidentialityImpact,
            profile.ModifiedConfidentialityImpact
        );
        var modifiedIntegrityImpact = ResolveImpact(
            parsed.IntegrityImpact,
            profile.ModifiedIntegrityImpact
        );
        var modifiedAvailabilityImpact = ResolveImpact(
            parsed.AvailabilityImpact,
            profile.ModifiedAvailabilityImpact
        );
        var confidentialityRequirement = RequirementMultiplier(profile.ConfidentialityRequirement);
        var integrityRequirement = RequirementMultiplier(profile.IntegrityRequirement);
        var availabilityRequirement = RequirementMultiplier(profile.AvailabilityRequirement);

        factors.Add(
            new(
                "EnvironmentClass",
                profile.EnvironmentClass.ToString(),
                "Reusable security profile environment class."
            )
        );
        factors.Add(
            new(
                "ModifiedExploitability",
                $"MAV:{profile.ModifiedAttackVector} MAC:{profile.ModifiedAttackComplexity} MPR:{profile.ModifiedPrivilegesRequired} MUI:{profile.ModifiedUserInteraction} MS:{profile.ModifiedScope}",
                "Explicit CVSS environmental modified exploitability metrics from the security profile."
            )
        );
        factors.Add(
            new(
                "ModifiedImpact",
                $"MC:{profile.ModifiedConfidentialityImpact} MI:{profile.ModifiedIntegrityImpact} MA:{profile.ModifiedAvailabilityImpact}",
                "Explicit CVSS environmental modified impact metrics from the security profile."
            )
        );
        factors.Add(
            new(
                "SecurityRequirements",
                $"C:{profile.ConfidentialityRequirement} I:{profile.IntegrityRequirement} A:{profile.AvailabilityRequirement}",
                "Mapped to environmental confidentiality, integrity, and availability requirements."
            )
        );

        var effectiveVector = parsed.ToModifiedVector(
            modifiedAttackVector,
            modifiedAttackComplexity,
            modifiedPrivilegesRequired,
            modifiedUserInteraction,
            modifiedScope,
            modifiedConfidentialityImpact,
            modifiedIntegrityImpact,
            modifiedAvailabilityImpact,
            confidentialityRequirement.LevelCode,
            integrityRequirement.LevelCode,
            availabilityRequirement.LevelCode
        );

        var effectiveScore = CalculateEnvironmentalScore(
            modifiedAttackVector,
            modifiedAttackComplexity,
            modifiedPrivilegesRequired,
            modifiedUserInteraction,
            modifiedScope,
            modifiedConfidentialityImpact,
            modifiedIntegrityImpact,
            modifiedAvailabilityImpact,
            confidentialityRequirement.Multiplier,
            integrityRequirement.Multiplier,
            availabilityRequirement.Multiplier
        );
        var effectiveSeverity = ToSeverity(effectiveScore);
        var reasonSummary = BuildReasonSummary(
            vulnerability.VendorSeverity,
            effectiveSeverity,
            profile,
            modifiedAttackVector,
            parsed.AttackVector,
            modifiedAttackComplexity,
            parsed.AttackComplexity,
            modifiedPrivilegesRequired,
            parsed.PrivilegesRequired,
            modifiedUserInteraction,
            parsed.UserInteraction,
            modifiedScope,
            parsed.Scope,
            modifiedConfidentialityImpact,
            parsed.ConfidentialityImpact,
            modifiedIntegrityImpact,
            parsed.IntegrityImpact,
            modifiedAvailabilityImpact,
            parsed.AvailabilityImpact
        );

        return new EnvironmentalSeverityCalculationResult(
            profile.Id,
            vulnerability.VendorSeverity,
            vulnerability.CvssScore,
            vulnerability.CvssVector,
            effectiveSeverity,
            effectiveScore,
            effectiveVector,
            JsonSerializer.Serialize(factors),
            reasonSummary
        );
    }

    private static decimal CalculateEnvironmentalScore(
        AttackVector modifiedAttackVector,
        AttackComplexity modifiedAttackComplexity,
        PrivilegesRequired modifiedPrivilegesRequired,
        UserInteraction modifiedUserInteraction,
        ScopeMetric modifiedScope,
        decimal modifiedConfidentialityImpact,
        decimal modifiedIntegrityImpact,
        decimal modifiedAvailabilityImpact,
        decimal confidentialityRequirement,
        decimal integrityRequirement,
        decimal availabilityRequirement
    )
    {
        var mc = modifiedConfidentialityImpact * confidentialityRequirement;
        var mi = modifiedIntegrityImpact * integrityRequirement;
        var ma = modifiedAvailabilityImpact * availabilityRequirement;
        var mcValue = (double)mc;
        var miValue = (double)mi;
        var maValue = (double)ma;
        var miss = Math.Min(1d - ((1d - mcValue) * (1d - miValue) * (1d - maValue)), 0.915d);

        var scopeChanged = modifiedScope == ScopeMetric.Changed;
        var modifiedImpact = scopeChanged
            ? 7.52d * (miss - 0.029d) - 3.25d * Math.Pow(miss * 0.9731d - 0.02d, 13d)
            : 6.42d * miss;

        if (modifiedImpact <= 0)
        {
            return 0m;
        }

        var exploitability =
            8.22d
            * AttackVectorWeight(modifiedAttackVector)
            * AttackComplexityWeight(modifiedAttackComplexity)
            * PrivilegesRequiredWeight(modifiedPrivilegesRequired, scopeChanged)
            * UserInteractionWeight(modifiedUserInteraction);

        var score = scopeChanged
            ? Math.Min(1.08d * (modifiedImpact + exploitability), 10d)
            : Math.Min(modifiedImpact + exploitability, 10d);

        return RoundUp1(score);
    }

    private static RequirementWeight RequirementMultiplier(SecurityRequirementLevel level)
    {
        return level switch
        {
            SecurityRequirementLevel.Low => new RequirementWeight(0.5m, "L"),
            SecurityRequirementLevel.High => new RequirementWeight(1.5m, "H"),
            _ => new RequirementWeight(1.0m, "M"),
        };
    }

    private static string BuildReasonSummary(
        Severity baseSeverity,
        Severity effectiveSeverity,
        AssetSecurityProfile profile,
        AttackVector modifiedAttackVector,
        AttackVector baseAttackVector,
        AttackComplexity modifiedAttackComplexity,
        AttackComplexity baseAttackComplexity,
        PrivilegesRequired modifiedPrivilegesRequired,
        PrivilegesRequired basePrivilegesRequired,
        UserInteraction modifiedUserInteraction,
        UserInteraction baseUserInteraction,
        ScopeMetric modifiedScope,
        ScopeMetric baseScope,
        decimal modifiedConfidentialityImpact,
        decimal baseConfidentialityImpact,
        decimal modifiedIntegrityImpact,
        decimal baseIntegrityImpact,
        decimal modifiedAvailabilityImpact,
        decimal baseAvailabilityImpact
    )
    {
        var reasons = new List<string>();

        if (modifiedAttackVector != baseAttackVector)
        {
            reasons.Add($"Modified attack vector changed from {baseAttackVector} to {modifiedAttackVector}");
        }

        if (modifiedAttackComplexity != baseAttackComplexity)
        {
            reasons.Add($"Modified attack complexity changed from {baseAttackComplexity} to {modifiedAttackComplexity}");
        }

        if (modifiedPrivilegesRequired != basePrivilegesRequired)
        {
            reasons.Add($"Modified privileges required changed from {basePrivilegesRequired} to {modifiedPrivilegesRequired}");
        }

        if (modifiedUserInteraction != baseUserInteraction)
        {
            reasons.Add($"Modified user interaction changed from {baseUserInteraction} to {modifiedUserInteraction}");
        }

        if (modifiedScope != baseScope)
        {
            reasons.Add($"Modified scope changed from {baseScope} to {modifiedScope}");
        }

        if (modifiedConfidentialityImpact != baseConfidentialityImpact)
        {
            reasons.Add("Modified confidentiality impact differs from the vendor vector");
        }

        if (modifiedIntegrityImpact != baseIntegrityImpact)
        {
            reasons.Add("Modified integrity impact differs from the vendor vector");
        }

        if (modifiedAvailabilityImpact != baseAvailabilityImpact)
        {
            reasons.Add("Modified availability impact differs from the vendor vector");
        }

        if (profile.ConfidentialityRequirement != SecurityRequirementLevel.Medium)
        {
            reasons.Add($"Confidentiality requirement is {profile.ConfidentialityRequirement}");
        }

        if (profile.IntegrityRequirement != SecurityRequirementLevel.Medium)
        {
            reasons.Add($"Integrity requirement is {profile.IntegrityRequirement}");
        }

        if (profile.AvailabilityRequirement != SecurityRequirementLevel.Medium)
        {
            reasons.Add($"Availability requirement is {profile.AvailabilityRequirement}");
        }

        if (reasons.Count == 0)
        {
            reasons.Add(
                "Profile settings kept the effective severity close to the vendor baseline"
            );
        }

        return $"{baseSeverity} -> {effectiveSeverity}. {string.Join("; ", reasons)}.";
    }

    private static Severity ToSeverity(decimal? score)
    {
        if (score is null)
        {
            return Severity.Medium;
        }

        return score.Value switch
        {
            >= 9.0m => Severity.Critical,
            >= 7.0m => Severity.High,
            >= 4.0m => Severity.Medium,
            _ => Severity.Low,
        };
    }

    private static decimal RoundUp1(double input)
    {
        return Math.Ceiling((decimal)input * 10m) / 10m;
    }

    private static double AttackVectorWeight(AttackVector attackVector) =>
        attackVector switch
        {
            AttackVector.Network => 0.85d,
            AttackVector.Adjacent => 0.62d,
            AttackVector.Local => 0.55d,
            AttackVector.Physical => 0.2d,
            _ => 0.85d,
        };

    private static double AttackComplexityWeight(AttackComplexity attackComplexity) =>
        attackComplexity == AttackComplexity.High ? 0.44d : 0.77d;

    private static double UserInteractionWeight(UserInteraction userInteraction) =>
        userInteraction == UserInteraction.Required ? 0.62d : 0.85d;

    private static double PrivilegesRequiredWeight(
        PrivilegesRequired privilegesRequired,
        bool scopeChanged
    )
    {
        return privilegesRequired switch
        {
            PrivilegesRequired.None => 0.85d,
            PrivilegesRequired.Low => scopeChanged ? 0.68d : 0.62d,
            PrivilegesRequired.High => scopeChanged ? 0.5d : 0.27d,
            _ => 0.85d,
        };
    }

    private sealed record RequirementWeight(decimal Multiplier, string LevelCode);

    public sealed record AssessmentFactor(string Key, string Value, string Explanation);

    public sealed record EnvironmentalSeverityCalculationResult(
        Guid? AssetSecurityProfileId,
        Severity BaseSeverity,
        decimal? BaseScore,
        string? BaseVector,
        Severity EffectiveSeverity,
        decimal? EffectiveScore,
        string? EffectiveVector,
        string FactorsJson,
        string ReasonSummary
    )
    {
        public static EnvironmentalSeverityCalculationResult FromBaseSeverity(
            Severity baseSeverity,
            decimal? baseScore,
            string? baseVector,
            Guid? assetSecurityProfileId,
            string reasonSummary,
            IReadOnlyList<AssessmentFactor>? factors = null
        )
        {
            return new EnvironmentalSeverityCalculationResult(
                assetSecurityProfileId,
                baseSeverity,
                baseScore,
                baseVector,
                baseSeverity,
                baseScore,
                baseVector,
                JsonSerializer.Serialize(factors ?? Array.Empty<AssessmentFactor>()),
                reasonSummary
            );
        }
    }

    private sealed class CvssVector
    {
        public AttackVector AttackVector { get; private init; }
        public AttackComplexity AttackComplexity { get; private init; }
        public PrivilegesRequired PrivilegesRequired { get; private init; }
        public UserInteraction UserInteraction { get; private init; }
        public ScopeMetric Scope { get; private init; }
        public decimal ConfidentialityImpact { get; private init; }
        public decimal IntegrityImpact { get; private init; }
        public decimal AvailabilityImpact { get; private init; }

        public static CvssVector? TryParse(string vector)
        {
            var parts = vector.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var map = parts
                .Skip(1)
                .Select(part => part.Split(':', 2))
                .Where(part => part.Length == 2)
                .ToDictionary(part => part[0], part => part[1], StringComparer.OrdinalIgnoreCase);

            if (
                !map.TryGetValue("AV", out var av)
                || !map.TryGetValue("AC", out var ac)
                || !map.TryGetValue("PR", out var pr)
                || !map.TryGetValue("UI", out var ui)
                || !map.TryGetValue("S", out var scope)
                || !map.TryGetValue("C", out var c)
                || !map.TryGetValue("I", out var i)
                || !map.TryGetValue("A", out var a)
            )
            {
                return null;
            }

            return new CvssVector
            {
                AttackVector = ParseAttackVector(av),
                AttackComplexity = ac == "H" ? AttackComplexity.High : AttackComplexity.Low,
                PrivilegesRequired = pr switch
                {
                    "L" => PrivilegesRequired.Low,
                    "H" => PrivilegesRequired.High,
                    _ => PrivilegesRequired.None,
                },
                UserInteraction = ui == "R" ? UserInteraction.Required : UserInteraction.None,
                Scope = scope == "C" ? ScopeMetric.Changed : ScopeMetric.Unchanged,
                ConfidentialityImpact = ParseImpact(c),
                IntegrityImpact = ParseImpact(i),
                AvailabilityImpact = ParseImpact(a),
            };
        }

        public string ToModifiedVector(
            AttackVector modifiedAttackVector,
            AttackComplexity modifiedAttackComplexity,
            PrivilegesRequired modifiedPrivilegesRequired,
            UserInteraction modifiedUserInteraction,
            ScopeMetric modifiedScope,
            decimal modifiedConfidentialityImpact,
            decimal modifiedIntegrityImpact,
            decimal modifiedAvailabilityImpact,
            string confidentialityRequirement,
            string integrityRequirement,
            string availabilityRequirement
        )
        {
            return string.Join(
                '/',
                new[]
                {
                    "CVSS:3.1",
                    $"MAV:{AttackVectorCode(modifiedAttackVector)}",
                    $"MAC:{AttackComplexityCode(modifiedAttackComplexity)}",
                    $"MPR:{PrivilegesRequiredCode(modifiedPrivilegesRequired)}",
                    $"MUI:{UserInteractionCode(modifiedUserInteraction)}",
                    $"MS:{ScopeCode(modifiedScope)}",
                    $"MC:{ImpactCode(modifiedConfidentialityImpact)}",
                    $"MI:{ImpactCode(modifiedIntegrityImpact)}",
                    $"MA:{ImpactCode(modifiedAvailabilityImpact)}",
                    $"CR:{confidentialityRequirement}",
                    $"IR:{integrityRequirement}",
                    $"AR:{availabilityRequirement}",
                }
            );
        }

        private static AttackVector ParseAttackVector(string value) =>
            value switch
            {
                "A" => AttackVector.Adjacent,
                "L" => AttackVector.Local,
                "P" => AttackVector.Physical,
                _ => AttackVector.Network,
            };

        private static decimal ParseImpact(string value) =>
            value switch
            {
                "L" => 0.22m,
                "H" => 0.56m,
                _ => 0m,
            };

        private static string AttackVectorCode(AttackVector attackVector) =>
            attackVector switch
            {
                AttackVector.Adjacent => "A",
                AttackVector.Local => "L",
                AttackVector.Physical => "P",
                _ => "N",
            };

        private static string AttackComplexityCode(AttackComplexity attackComplexity) =>
            attackComplexity == AttackComplexity.High ? "H" : "L";

        private static string PrivilegesRequiredCode(PrivilegesRequired privilegesRequired) =>
            privilegesRequired switch
            {
                PrivilegesRequired.Low => "L",
                PrivilegesRequired.High => "H",
                _ => "N",
            };

        private static string UserInteractionCode(UserInteraction userInteraction) =>
            userInteraction == UserInteraction.Required ? "R" : "N";

        private static string ScopeCode(ScopeMetric scope) =>
            scope == ScopeMetric.Changed ? "C" : "U";

        private static string ImpactCode(decimal impact) =>
            impact switch
            {
                0.22m => "L",
                0.56m => "H",
                _ => "N",
            };
    }

    private static AttackVector ResolveAttackVector(
        AttackVector baseAttackVector,
        CvssModifiedAttackVector modifiedAttackVector
    ) =>
        modifiedAttackVector switch
        {
            CvssModifiedAttackVector.Network => AttackVector.Network,
            CvssModifiedAttackVector.Adjacent => AttackVector.Adjacent,
            CvssModifiedAttackVector.Local => AttackVector.Local,
            CvssModifiedAttackVector.Physical => AttackVector.Physical,
            _ => baseAttackVector,
        };

    private static AttackComplexity ResolveAttackComplexity(
        AttackComplexity baseAttackComplexity,
        CvssModifiedAttackComplexity modifiedAttackComplexity
    ) => modifiedAttackComplexity == CvssModifiedAttackComplexity.High ? AttackComplexity.High :
        modifiedAttackComplexity == CvssModifiedAttackComplexity.Low ? AttackComplexity.Low :
        baseAttackComplexity;

    private static PrivilegesRequired ResolvePrivilegesRequired(
        PrivilegesRequired basePrivilegesRequired,
        CvssModifiedPrivilegesRequired modifiedPrivilegesRequired
    ) => modifiedPrivilegesRequired switch
    {
        CvssModifiedPrivilegesRequired.None => PrivilegesRequired.None,
        CvssModifiedPrivilegesRequired.Low => PrivilegesRequired.Low,
        CvssModifiedPrivilegesRequired.High => PrivilegesRequired.High,
        _ => basePrivilegesRequired,
    };

    private static UserInteraction ResolveUserInteraction(
        UserInteraction baseUserInteraction,
        CvssModifiedUserInteraction modifiedUserInteraction
    ) => modifiedUserInteraction == CvssModifiedUserInteraction.Required ? UserInteraction.Required :
        modifiedUserInteraction == CvssModifiedUserInteraction.None ? UserInteraction.None :
        baseUserInteraction;

    private static ScopeMetric ResolveScope(ScopeMetric baseScope, CvssModifiedScope modifiedScope) =>
        modifiedScope == CvssModifiedScope.Changed ? ScopeMetric.Changed :
        modifiedScope == CvssModifiedScope.Unchanged ? ScopeMetric.Unchanged :
        baseScope;

    private static decimal ResolveImpact(decimal baseImpact, CvssModifiedImpact modifiedImpact) =>
        modifiedImpact switch
        {
            CvssModifiedImpact.Low => 0.22m,
            CvssModifiedImpact.High => 0.56m,
            CvssModifiedImpact.None => 0m,
            _ => baseImpact,
        };

    private enum AttackVector
    {
        Network,
        Adjacent,
        Local,
        Physical,
    }

    private enum AttackComplexity
    {
        Low,
        High,
    }

    private enum PrivilegesRequired
    {
        None,
        Low,
        High,
    }

    private enum UserInteraction
    {
        None,
        Required,
    }

    private enum ScopeMetric
    {
        Unchanged,
        Changed,
    }
}

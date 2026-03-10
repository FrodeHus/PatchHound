using System.Text.Json;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Infrastructure.Services;

public class EnvironmentalSeverityCalculator
{
    public const string CalculationVersion = "1";

    public EnvironmentalSeverityCalculationResult Calculate(
        VulnerabilityDefinition vulnerability,
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

        var modifiedAttackVector = ApplyReachability(
            parsed.AttackVector,
            profile.InternetReachability
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
                "InternetReachability",
                profile.InternetReachability.ToString(),
                "Mapped to the modified attack vector during environmental scoring."
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
            confidentialityRequirement.LevelCode,
            integrityRequirement.LevelCode,
            availabilityRequirement.LevelCode
        );

        var effectiveScore = CalculateEnvironmentalScore(
            parsed,
            modifiedAttackVector,
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
            parsed.AttackVector
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
        CvssVector parsed,
        AttackVector modifiedAttackVector,
        decimal confidentialityRequirement,
        decimal integrityRequirement,
        decimal availabilityRequirement
    )
    {
        var mc = parsed.ConfidentialityImpact * confidentialityRequirement;
        var mi = parsed.IntegrityImpact * integrityRequirement;
        var ma = parsed.AvailabilityImpact * availabilityRequirement;
        var mcValue = (double)mc;
        var miValue = (double)mi;
        var maValue = (double)ma;
        var miss = Math.Min(1d - ((1d - mcValue) * (1d - miValue) * (1d - maValue)), 0.915d);

        var scopeChanged = parsed.Scope == ScopeMetric.Changed;
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
            * AttackComplexityWeight(parsed.AttackComplexity)
            * PrivilegesRequiredWeight(parsed.PrivilegesRequired, scopeChanged)
            * UserInteractionWeight(parsed.UserInteraction);

        var score = scopeChanged
            ? Math.Min(1.08d * (modifiedImpact + exploitability), 10d)
            : Math.Min(modifiedImpact + exploitability, 10d);

        return RoundUp1(score);
    }

    private static AttackVector ApplyReachability(
        AttackVector baseAttackVector,
        InternetReachability reachability
    )
    {
        var maximumReachability = reachability switch
        {
            InternetReachability.Internet => AttackVector.Network,
            InternetReachability.InternalNetwork => AttackVector.Network,
            InternetReachability.AdjacentOnly => AttackVector.Adjacent,
            InternetReachability.LocalOnly => AttackVector.Local,
            _ => baseAttackVector,
        };

        return MoreRestrictive(baseAttackVector, maximumReachability);
    }

    private static AttackVector MoreRestrictive(AttackVector left, AttackVector right)
    {
        return Restrictiveness(left) >= Restrictiveness(right) ? left : right;
    }

    private static int Restrictiveness(AttackVector attackVector) =>
        attackVector switch
        {
            AttackVector.Physical => 4,
            AttackVector.Local => 3,
            AttackVector.Adjacent => 2,
            _ => 1,
        };

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
        AttackVector baseAttackVector
    )
    {
        var reasons = new List<string>();

        if (modifiedAttackVector != baseAttackVector)
        {
            reasons.Add(
                $"Reachability reduced attack vector from {baseAttackVector} to {modifiedAttackVector}"
            );
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
    }

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

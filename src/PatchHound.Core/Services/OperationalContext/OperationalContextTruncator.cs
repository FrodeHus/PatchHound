using System.Text.Json;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models.OperationalContext;

namespace PatchHound.Core.Services.OperationalContext;

public sealed class OperationalContextTruncator(IPromptTokenEstimator estimator)
{
    /// <summary>
    /// Trims <paramref name="pack"/> to fit within <paramref name="maxTokens"/> by progressively
    /// dropping lower-priority detail (workflow, labels, teams, citations).
    /// <para>
    /// This method mutates <paramref name="pack"/> in place and returns the same reference.
    /// Callers must not retain the pre-truncation pack expecting it to remain unchanged.
    /// </para>
    /// </summary>
    public (OperationalContextPack Pack, int TokenEstimate) Fit(
        OperationalContextPack pack, int maxTokens)
    {
        if (Within(pack, maxTokens, out var tokens))
        {
            pack.Limits.Truncated = false;
            return (pack, tokens);
        }

        pack.Limits.Truncated = true;

        // 1) drop workflow detail
        pack.Workflow = null;
        if (Within(pack, maxTokens, out tokens)) return (pack, tokens);

        // 2) trim business labels to first
        if (pack.Scope.TopBusinessLabels.Count > 1)
            pack.Scope.TopBusinessLabels = pack.Scope.TopBusinessLabels.Take(1).ToList();
        if (Within(pack, maxTokens, out tokens)) return (pack, tokens);

        // 3) trim teams and profiles to first
        if (pack.Scope.TopOwnerTeams.Count > 1)
            pack.Scope.TopOwnerTeams = pack.Scope.TopOwnerTeams.Take(1).ToList();
        if (pack.Scope.TopSecurityProfiles.Count > 1)
            pack.Scope.TopSecurityProfiles = pack.Scope.TopSecurityProfiles.Take(1).ToList();
        if (Within(pack, maxTokens, out tokens)) return (pack, tokens);

        // 4) drop citations lowest-risk-first, keep at least one if it fits
        var ordered = pack.Citations.OrderByDescending(c => c.RiskWeight).ToList();
        while (ordered.Count > 1)
        {
            ordered.RemoveAt(ordered.Count - 1);
            pack.Citations = ordered.ToList();
            if (Within(pack, maxTokens, out tokens)) return (pack, tokens);
        }

        // 5) drop the last citation too if still over budget
        if (ordered.Count == 1)
        {
            pack.Citations = [];
            if (Within(pack, maxTokens, out tokens)) return (pack, tokens);
        }

        Within(pack, maxTokens, out tokens);
        return (pack, tokens);
    }

    private bool Within(OperationalContextPack pack, int maxTokens, out int tokens)
    {
        var json = JsonSerializer.Serialize(pack, OperationalContextPack.SerializerOptions);
        tokens = estimator.Estimate(json);
        return tokens <= maxTokens;
    }
}

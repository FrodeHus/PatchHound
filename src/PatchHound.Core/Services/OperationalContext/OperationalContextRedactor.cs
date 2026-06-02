using System.Text.RegularExpressions;
using PatchHound.Core.Models.OperationalContext;

namespace PatchHound.Core.Services.OperationalContext;

public sealed class OperationalContextRedactor
{
    public IReadOnlyList<OperationalContextCitation> RedactCitations(
        IReadOnlyList<OperationalContextCitation> citations,
        AiOperationalContextOptions options)
    {
        if (!options.RedactDeviceNames)
        {
            return citations;
        }

        var result = new List<OperationalContextCitation>(citations.Count);
        var deviceOrdinal = 0;
        foreach (var c in citations)
        {
            if (!string.Equals(c.EntityType, "Device", StringComparison.Ordinal))
            {
                result.Add(c);
                continue;
            }

            deviceOrdinal++;
            var pseudonym = $"device-{deviceOrdinal}";
            var fact = string.IsNullOrWhiteSpace(c.Label)
                ? c.Fact
                : Regex.Replace(c.Fact, $@"(?<!\w){Regex.Escape(c.Label)}(?!\w)", pseudonym);
            result.Add(new OperationalContextCitation
            {
                Key = c.Key,
                EntityType = c.EntityType,
                EntityId = c.EntityId,
                Label = pseudonym,
                Fact = fact,
                RiskWeight = c.RiskWeight,
            });
        }

        return result;
    }
}

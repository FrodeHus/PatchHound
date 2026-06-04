using PatchHound.Core.Models.OperationalContext;

namespace PatchHound.Core.Services.OperationalContext;

public sealed record CitationValidationResult(
    IReadOnlyList<OperationalContextCitation> Citations,
    bool Uncited);

public static class OperationalContextCitationValidator
{
    /// <summary>
    /// Returns the pack citations whose key the model cited (in pack order), dropping unknown
    /// keys. <see cref="CitationValidationResult.Uncited"/> is true when nothing valid remains —
    /// the caller should mark the generated output as uncited (spec citation rule).
    /// </summary>
    public static CitationValidationResult Validate(
        IReadOnlyList<string> modelKeys,
        IReadOnlyList<OperationalContextCitation> pack)
    {
        var cited = new HashSet<string>(modelKeys, StringComparer.Ordinal);
        var kept = pack.Where(c => cited.Contains(c.Key)).ToList();
        return new CitationValidationResult(kept, kept.Count == 0);
    }
}

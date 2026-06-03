using PatchHound.Core.Interfaces;

namespace PatchHound.Core.Services.OperationalContext;

public sealed class HeuristicPromptTokenEstimator : IPromptTokenEstimator
{
    public int Estimate(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);
}

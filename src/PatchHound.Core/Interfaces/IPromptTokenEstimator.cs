namespace PatchHound.Core.Interfaces;

/// <summary>
/// Estimates prompt token count for the operational-context budget guardrail. The default
/// implementation is a provider-agnostic heuristic; provider-specific tokenizers may be added
/// later behind this interface without changing callers.
/// </summary>
public interface IPromptTokenEstimator
{
    int Estimate(string text);
}

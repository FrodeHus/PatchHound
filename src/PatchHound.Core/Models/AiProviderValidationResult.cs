namespace PatchHound.Core.Models;

public record AiProviderValidationResult(bool IsSuccess, string Error)
{
    public static AiProviderValidationResult Success() => new(true, string.Empty);

    public static AiProviderValidationResult Failure(string error) => new(false, error);
}

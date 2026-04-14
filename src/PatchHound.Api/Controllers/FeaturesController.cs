using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;
using PatchHound.Core.Common;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/features")]
[Authorize]
public class FeaturesController : ControllerBase
{
    private readonly IFeatureManager _featureManager;

    public FeaturesController(IFeatureManager featureManager)
    {
        _featureManager = featureManager;
    }

    [HttpGet]
    public async Task<ActionResult<Dictionary<string, FeatureFlagResponseDto>>> GetFeatures(
        CancellationToken ct
    )
    {
        var result = new Dictionary<string, FeatureFlagResponseDto>();

        foreach (var (key, meta) in FeatureFlags.Metadata)
        {
            var isEnabled = await _featureManager.IsEnabledAsync(key);
            result[key] = new FeatureFlagResponseDto(meta.DisplayName, meta.Stage.ToString(), isEnabled);
        }

        return Ok(result);
    }
}

public record FeatureFlagResponseDto(string DisplayName, string Stage, bool IsEnabled);

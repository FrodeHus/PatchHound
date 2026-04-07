using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;
using PatchHound.Core.Common;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/features")]
[Authorize]
public class FeaturesController(IFeatureManager featureManager) : ControllerBase
{
    public record FeatureFlagsDto(bool Workflows, bool AuthenticatedScans);

    [HttpGet]
    public async Task<ActionResult<FeatureFlagsDto>> List()
    {
        return new FeatureFlagsDto(
            await featureManager.IsEnabledAsync(FeatureFlags.Workflows),
            await featureManager.IsEnabledAsync(FeatureFlags.AuthenticatedScans)
        );
    }
}

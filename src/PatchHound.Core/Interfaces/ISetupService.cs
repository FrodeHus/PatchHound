using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface ISetupService
{
    Task<bool> IsInitializedAsync(CancellationToken ct);
    Task<Result<Tenant>> CompleteSetupAsync(SetupRequest request, CancellationToken ct);
}

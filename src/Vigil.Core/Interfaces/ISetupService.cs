using Vigil.Core.Common;
using Vigil.Core.Entities;
using Vigil.Core.Models;

namespace Vigil.Core.Interfaces;

public interface ISetupService
{
    Task<bool> IsInitializedAsync(CancellationToken ct);
    Task<Result<Tenant>> CompleteSetupAsync(SetupRequest request, CancellationToken ct);
}

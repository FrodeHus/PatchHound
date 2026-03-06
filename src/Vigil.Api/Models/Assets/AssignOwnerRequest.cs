namespace Vigil.Api.Models.Assets;

public record AssignOwnerRequest(string OwnerType, Guid OwnerId);

public record SetCriticalityRequest(string Criticality);

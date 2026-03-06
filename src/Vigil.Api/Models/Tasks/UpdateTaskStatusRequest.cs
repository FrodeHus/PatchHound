namespace Vigil.Api.Models.Tasks;

public record UpdateTaskStatusRequest(
    string Status,
    string? Justification = null);

public record TaskFilterQuery(
    string? Status = null,
    Guid? TenantId = null,
    Guid? AssigneeId = null);

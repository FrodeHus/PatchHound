namespace PatchHound.Api.Models.Workflows;

public record WorkflowDefinitionDto(
    Guid Id,
    Guid? TenantId,
    string Name,
    string? Description,
    string Scope,
    string TriggerType,
    int Version,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record WorkflowDefinitionDetailDto(
    Guid Id,
    Guid? TenantId,
    string Name,
    string? Description,
    string Scope,
    string TriggerType,
    int Version,
    string Status,
    string GraphJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CreatedBy
);

public record CreateWorkflowDefinitionRequest(
    Guid? TenantId,
    string Name,
    string? Description,
    string Scope,
    string TriggerType,
    string GraphJson
);

public record UpdateWorkflowDefinitionRequest(
    string Name,
    string? Description,
    string GraphJson
);

public record WorkflowInstanceDto(
    Guid Id,
    Guid WorkflowDefinitionId,
    string WorkflowName,
    int DefinitionVersion,
    Guid? TenantId,
    string TriggerType,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error
);

public record WorkflowInstanceDetailDto(
    Guid Id,
    Guid WorkflowDefinitionId,
    string WorkflowName,
    int DefinitionVersion,
    Guid? TenantId,
    string TriggerType,
    string ContextJson,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error,
    IReadOnlyList<WorkflowNodeExecutionDto> NodeExecutions
);

public record WorkflowNodeExecutionDto(
    Guid Id,
    string NodeId,
    string NodeType,
    string Status,
    string? InputJson,
    string? OutputJson,
    string? Error,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    Guid? AssignedTeamId,
    Guid? CompletedByUserId
);

public record WorkflowActionDto(
    Guid Id,
    Guid WorkflowInstanceId,
    Guid NodeExecutionId,
    Guid TenantId,
    Guid TeamId,
    string ActionType,
    string? Instructions,
    string Status,
    string? ResponseJson,
    DateTimeOffset? DueAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    Guid? CompletedByUserId,
    string? WorkflowName,
    string? ContextJson
);

public record CompleteWorkflowActionRequest(string? ResponseJson);

public record RejectWorkflowActionRequest(string? ResponseJson);

public record RunWorkflowRequest(string? ContextJson);

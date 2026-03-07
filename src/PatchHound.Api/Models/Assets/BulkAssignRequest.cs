namespace PatchHound.Api.Models.Assets;

public record BulkAssignRequest(List<Guid> AssetIds, string OwnerType, Guid OwnerId);

public record BulkAssignResponse(int UpdatedCount);

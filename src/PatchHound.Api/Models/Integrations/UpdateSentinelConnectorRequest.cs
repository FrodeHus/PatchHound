using System.ComponentModel.DataAnnotations;

namespace PatchHound.Api.Models.Integrations;

public record UpdateSentinelConnectorRequest(
    bool Enabled,
    [MaxLength(512)] string DceEndpoint,
    [MaxLength(256)] string DcrImmutableId,
    [MaxLength(256)] string StreamName,
    Guid? StoredCredentialId
);

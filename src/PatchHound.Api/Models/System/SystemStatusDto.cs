namespace PatchHound.Api.Models.System;

public record SystemStatusDto(bool OpenBaoAvailable, bool OpenBaoInitialized, bool OpenBaoSealed);

public record OpenBaoUnsealRequest(IReadOnlyList<string> Keys);

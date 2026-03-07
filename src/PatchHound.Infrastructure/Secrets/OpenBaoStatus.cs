namespace PatchHound.Infrastructure.Secrets;

public record OpenBaoStatus(bool IsAvailable, bool IsInitialized, bool IsSealed);

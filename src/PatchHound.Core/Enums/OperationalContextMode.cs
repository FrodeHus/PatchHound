namespace PatchHound.Core.Enums;

public enum OperationalContextMode
{
    Disabled = 0,
    StructuredOnly = 1,

    /// <summary>
    /// Reserved for P2 semantic retrieval. Until embeddings exist this behaves exactly like
    /// <see cref="StructuredOnly"/>.
    /// </summary>
    StructuredAndSemantic = 2,
}

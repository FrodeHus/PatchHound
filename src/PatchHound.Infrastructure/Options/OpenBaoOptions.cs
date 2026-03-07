using System.ComponentModel.DataAnnotations;

namespace PatchHound.Infrastructure.Options;

public class OpenBaoOptions
{
    public const string SectionName = "OpenBao";

    [Required]
    public string Address { get; set; } = "http://openbao:8200";
    public string Token { get; set; } = string.Empty;
    public string KvMount { get; set; } = "patchhound";
}

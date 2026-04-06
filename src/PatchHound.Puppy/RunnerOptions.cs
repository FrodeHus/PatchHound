using System.ComponentModel.DataAnnotations;

namespace PatchHound.Puppy;

public class RunnerOptions
{
    [Required]
    public string CentralUrl { get; set; } = string.Empty;

    [Required]
    public string BearerToken { get; set; } = string.Empty;

    public int MaxConcurrentJobs { get; set; } = 10;

    public int PollIntervalSeconds { get; set; } = 10;

    public int HeartbeatIntervalSeconds { get; set; } = 30;

    public string Hostname { get; set; } = Environment.MachineName;
}

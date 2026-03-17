namespace PatchHound.Api.Services;

public static class SoftwareCorrelationRanker
{
    public record SoftwareInstallationInput(
        string Name,
        int EpisodeNumber,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset? RemovedAt
    );

    public record VulnerabilityEpisodeInput(int EpisodeNumber, DateTimeOffset FirstSeenAt);

    public static IReadOnlyList<string> Rank(
        IEnumerable<SoftwareInstallationInput> softwareInstallations,
        IReadOnlyList<VulnerabilityEpisodeInput> episodes
    )
    {
        return softwareInstallations
            .Select(software =>
            {
                var best = episodes
                    .Where(episode =>
                        software.FirstSeenAt <= episode.FirstSeenAt
                        && (
                            software.RemovedAt is null
                            || software.RemovedAt >= episode.FirstSeenAt
                        )
                    )
                    .Select(episode =>
                    {
                        var age = episode.FirstSeenAt - software.FirstSeenAt;
                        var score = 0;

                        if (software.EpisodeNumber > 1)
                        {
                            score += 200;
                        }

                        if (episode.EpisodeNumber > 1)
                        {
                            score += 100;
                        }

                        score += age.TotalDays switch
                        {
                            <= 1 => 80,
                            <= 7 => 50,
                            <= 30 => 20,
                            _ => 0,
                        };

                        return new { software.Name, Score = score, Age = age };
                    })
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Age)
                    .FirstOrDefault();

                return best;
            })
            .Where(item => item is not null)
            .GroupBy(item => item!.Name, StringComparer.Ordinal)
            .Select(group =>
                group.OrderByDescending(item => item!.Score).ThenBy(item => item!.Age).First()
            )
            .OrderByDescending(item => item!.Score)
            .ThenBy(item => item!.Age)
            .Select(item => item!.Name)
            .ToList();
    }
}

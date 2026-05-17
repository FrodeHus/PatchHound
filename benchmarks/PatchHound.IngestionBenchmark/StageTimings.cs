namespace PatchHound.IngestionBenchmark;

public sealed record StageTimings(
    TimeSpan SeedExcluded,
    TimeSpan Merge,
    TimeSpan ProcessStaged,
    TimeSpan ExposureDerivation,
    TimeSpan EpisodeSync,
    TimeSpan SoftwareProjection,
    TimeSpan Total)
{
    public void PrintTo(TextWriter writer, int runIndex, BenchmarkOptions opts)
    {
        static string Fmt(TimeSpan t) => $"{t.TotalMilliseconds,9:N0} ms";
        writer.WriteLine($"Run #{runIndex + 1} of {opts.Runs}");
        writer.WriteLine($"  Device merge         {Fmt(Merge)}");
        writer.WriteLine($"  Process staged       {Fmt(ProcessStaged)}");
        writer.WriteLine($"  Exposure derivation  {Fmt(ExposureDerivation)}");
        writer.WriteLine($"  Episode sync         {Fmt(EpisodeSync)}");
        writer.WriteLine($"  Software projection  {Fmt(SoftwareProjection)}");
        writer.WriteLine($"  --------------------------------");
        writer.WriteLine($"  TOTAL                {Fmt(Total)}");
        writer.WriteLine($"  (Seed time, excluded from totals: {Fmt(SeedExcluded)})");
    }
}

using PatchHound.Puppy;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var builder = Host.CreateApplicationBuilder(args);

// Load runner.yaml from working directory or next to the binary
var yamlPath = FindConfigFile("runner.yaml");
if (yamlPath is null)
{
    Console.Error.WriteLine("ERROR: runner.yaml not found. Place it next to the binary or in the working directory.");
    return 1;
}

var yaml = File.ReadAllText(yamlPath);
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var options = deserializer.Deserialize<RunnerOptions>(yaml);

builder.Services.AddSingleton(options);

var host = builder.Build();

Console.WriteLine($"[startup] PatchHound.Puppy configured — central: {options.CentralUrl}, concurrency: {options.MaxConcurrentJobs}");
await host.RunAsync();
return 0;

static string? FindConfigFile(string fileName)
{
    // Check working directory first, then next to the binary
    var cwd = Path.Combine(Directory.GetCurrentDirectory(), fileName);
    if (File.Exists(cwd)) return cwd;

    var binDir = Path.Combine(AppContext.BaseDirectory, fileName);
    if (File.Exists(binDir)) return binDir;

    return null;
}

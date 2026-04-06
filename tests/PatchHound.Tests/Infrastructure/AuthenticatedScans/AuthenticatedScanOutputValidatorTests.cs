using PatchHound.Infrastructure.AuthenticatedScans;
using Xunit;

namespace PatchHound.Tests.Infrastructure.AuthenticatedScans;

public class AuthenticatedScanOutputValidatorTests
{
    private readonly AuthenticatedScanOutputValidator _sut = new();

    [Fact]
    public void Validate_returns_all_entries_when_valid()
    {
        var json = """{"software":[{"name":"nginx","vendor":"nginx","version":"1.24.0","installPath":"/usr/sbin/nginx"}]}""";
        var result = _sut.Validate(json);
        Assert.Empty(result.Issues);
        Assert.Single(result.ValidEntries);
        Assert.Equal("nginx", result.ValidEntries[0].Name);
        Assert.Equal("nginx", result.ValidEntries[0].Vendor);
        Assert.Equal("1.24.0", result.ValidEntries[0].Version);
        Assert.Equal("/usr/sbin/nginx", result.ValidEntries[0].InstallPath);
    }

    [Fact]
    public void Validate_accepts_entry_with_only_required_name()
    {
        var json = """{"software":[{"name":"openssl"}]}""";
        var result = _sut.Validate(json);
        Assert.Empty(result.Issues);
        Assert.Single(result.ValidEntries);
        Assert.Equal("openssl", result.ValidEntries[0].Name);
        Assert.Null(result.ValidEntries[0].Vendor);
        Assert.Null(result.ValidEntries[0].Version);
    }

    [Fact]
    public void Validate_flags_missing_required_name_and_keeps_valid_entries()
    {
        var json = """
        {"software":[
          {"name":"nginx","version":"1.24.0"},
          {"version":"2.0"},
          {"name":""}
        ]}
        """;
        var result = _sut.Validate(json);
        Assert.Single(result.ValidEntries);
        Assert.Equal(2, result.Issues.Count);
        Assert.Contains(result.Issues, i => i.EntryIndex == 1 && i.FieldPath.Contains("name"));
        Assert.Contains(result.Issues, i => i.EntryIndex == 2 && i.FieldPath.Contains("name"));
    }

    [Fact]
    public void Validate_rejects_missing_software_array()
    {
        var json = """{"notSoftware":[]}""";
        var result = _sut.Validate(json);
        Assert.True(result.FatalError);
        Assert.Contains("software", result.FatalErrorMessage);
    }

    [Fact]
    public void Validate_rejects_non_array_software()
    {
        var result = _sut.Validate("""{"software":"not-an-array"}""");
        Assert.True(result.FatalError);
    }

    [Fact]
    public void Validate_rejects_entry_count_over_5000()
    {
        var entries = string.Join(",", Enumerable.Range(0, 5001)
            .Select(i => $$"""{"name":"pkg{{i}}"}"""));
        var json = $$"""{"software":[{{entries}}]}""";
        var result = _sut.Validate(json);
        Assert.True(result.FatalError);
        Assert.Contains("5000", result.FatalErrorMessage);
    }

    [Fact]
    public void Validate_rejects_string_over_1024_chars()
    {
        var longStr = new string('x', 1025);
        var json = $$"""{"software":[{"name":"{{longStr}}"}]}""";
        var result = _sut.Validate(json);
        Assert.Empty(result.ValidEntries);
        Assert.Single(result.Issues);
        Assert.Contains("1024", result.Issues[0].Message);
    }

    [Fact]
    public void Validate_rejects_invalid_json()
    {
        var result = _sut.Validate("not json");
        Assert.True(result.FatalError);
    }
}

using PatchHound.Infrastructure.Data.Configurations;

namespace PatchHound.Tests.Infrastructure;

public class CloudApplicationConfigurationTests
{
    [Fact]
    public void DeserializeRedirectUris_ReturnsArrayValues_WhenJsonArrayIsProvided()
    {
        var result = CloudApplicationConfiguration.DeserializeRedirectUris(
            "[\"https://app.example/callback\",\"https://app.example/signout\"]"
        );

        result.Should()
            .BeEquivalentTo(
                ["https://app.example/callback", "https://app.example/signout"],
                options => options.WithStrictOrdering()
            );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData("\"https://app.example/callback\"")]
    public void DeserializeRedirectUris_ReturnsEmptyList_WhenLegacyScalarOrBlankValueIsProvided(
        string? storedValue
    )
    {
        var result = CloudApplicationConfiguration.DeserializeRedirectUris(storedValue);

        result.Should().BeEmpty();
    }
}

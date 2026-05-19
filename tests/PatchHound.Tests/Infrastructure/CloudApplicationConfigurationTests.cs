using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
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

    [Fact]
    public void ModelConfiguration_DefinesEfWarningSuppressingMetadata()
    {
        using var db = TestDbContextFactory.CreateSystemContext();

        var cloudApplication = db.Model.FindEntityType(typeof(CloudApplication));
        var credential = db.Model.FindEntityType(typeof(CloudApplicationCredentialMetadata));
        var businessLabel = db.Model.FindEntityType(typeof(BusinessLabel));

        cloudApplication.Should().NotBeNull();
        credential.Should().NotBeNull();
        businessLabel.Should().NotBeNull();

        cloudApplication!.FindProperty(nameof(CloudApplication.RedirectUris))!
            .GetValueComparer()
            .Should()
            .NotBeNull();
        credential!.GetDeclaredQueryFilters().Should().NotBeEmpty();
        businessLabel!.FindProperty(nameof(BusinessLabel.WeightCategory))!
            .Sentinel
            .Should()
            .Be((BusinessLabelWeightCategory)(-1));
    }
}

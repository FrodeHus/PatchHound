using System.Text;
using System.Text.Json;
using FluentAssertions;
using PatchHound.Api.Startup;

namespace PatchHound.Tests.Api;

public class JwtBearerTokenDiagnosticsReaderTests
{
    [Fact]
    public void TryRead_ReturnsClaimsFromBearerTokenPayload()
    {
        var token = BuildUnsignedJwt(new
        {
            aud = new[] { "api://patchhound", "client-id" },
            azp = "authorized-party",
            appid = "application-id",
        });

        var diagnostics = JwtBearerTokenDiagnosticsReader.TryRead($"Bearer {token}");

        diagnostics.Should().NotBeNull();
        diagnostics!.Audience.Should().Be("api://patchhound, client-id");
        diagnostics.AuthorizedParty.Should().Be("authorized-party");
        diagnostics.AppId.Should().Be("application-id");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer not-a-jwt")]
    public void TryRead_ReturnsNullForInvalidAuthorizationHeader(string authorizationHeader)
    {
        var diagnostics = JwtBearerTokenDiagnosticsReader.TryRead(authorizationHeader);

        diagnostics.Should().BeNull();
    }

    private static string BuildUnsignedJwt(object payload)
    {
        return string.Join(
            ".",
            Base64UrlEncode("{}"),
            Base64UrlEncode(JsonSerializer.Serialize(payload)),
            string.Empty
        );
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

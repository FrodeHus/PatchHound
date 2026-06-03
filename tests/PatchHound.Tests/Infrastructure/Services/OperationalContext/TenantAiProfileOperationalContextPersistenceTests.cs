using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services.OperationalContext;

[Collection(PostgresCollection.Name)]
public class TenantAiProfileOperationalContextPersistenceTests
{
    private static readonly Guid DefaultTenantId =
        Guid.Parse("00000001-0000-0000-0000-000000000001");

    private readonly PostgresFixture _fx;

    public TenantAiProfileOperationalContextPersistenceTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task NonDefault_operational_context_settings_round_trip_through_the_database()
    {
        await _fx.ResetAsync();

        // Non-default CLR values that EF must persist verbatim: Disabled (enum 0) and false.
        var profile = TenantAiProfile.Create(
            tenantId: DefaultTenantId,
            name: "Operational Context Profile",
            providerType: TenantAiProviderType.OpenAi,
            isDefault: true,
            isEnabled: true,
            model: "gpt-4.1-mini",
            systemPrompt: "Prompt",
            temperature: 0.2m,
            topP: null,
            maxOutputTokens: 1200,
            timeoutSeconds: 60,
            operationalContextMode: OperationalContextMode.Disabled,
            includeDeviceNamesInContext: false
        );

        await using (var db = _fx.CreateDbContext())
        {
            db.TenantAiProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        await using var reload = _fx.CreateDbContext();
        var loaded = reload
            .TenantAiProfiles
            .AsNoTracking()
            .First(p => p.Id == profile.Id);

        loaded.IncludeDeviceNamesInContext.Should().BeFalse();
        loaded.OperationalContextMode.Should().Be(OperationalContextMode.Disabled);
    }
}

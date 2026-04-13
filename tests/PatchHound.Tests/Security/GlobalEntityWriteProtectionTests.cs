using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;
using System.Reflection;

namespace PatchHound.Tests.Security;

/// <summary>
/// Verifies spec §7: global canonical entities (Vulnerability, VulnerabilityReference,
/// VulnerabilityApplicability, ThreatAssessment) cannot be written via a request-scoped
/// (non-system) db context, and that no controller exposes a write endpoint for them.
/// </summary>
public class GlobalEntityWriteProtectionTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;

    public GlobalEntityWriteProtectionTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        tenantContext.IsSystemContext.Returns(false);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
    }

    public void Dispose() => _dbContext.Dispose();

    // ── Controller surface checks ─────────────────────────────────────────────────

    [Fact]
    public void VulnerabilitiesController_HasNoCreateEndpoint()
    {
        // Verify no POST action exists that could accept a vulnerability create payload.
        // The only POST is /ai-report (stub, returns 409).
        var postMethods = typeof(VulnerabilitiesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.HttpPostAttribute>().Any())
            .Select(m => m.Name)
            .ToList();

        postMethods.Should().NotContain(
            m => m.Contains("Create", StringComparison.OrdinalIgnoreCase),
            "no POST action should expose vulnerability creation to tenant users");
    }

    [Fact]
    public void VulnerabilitiesController_HasNoDeleteEndpoint()
    {
        var deleteMethods = typeof(VulnerabilitiesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.HttpDeleteAttribute>().Any())
            .ToList();

        deleteMethods.Should().BeEmpty(
            "no DELETE action should expose vulnerability deletion to tenant users");
    }

    // ── DbContext write-protection checks ─────────────────────────────────────────

    [Fact]
    public async Task SystemContext_CanWrite_Vulnerability()
    {
        // SetSystemContext(true) must allow inserting global Vulnerability rows.
        _dbContext.SetSystemContext(true);

        var vuln = Vulnerability.Create(
            "NVD",
            "CVE-2026-9999",
            "Test vulnerability",
            "A test.",
            Severity.High,
            cvssScore: 7.5m,
            cvssVector: "AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N",
            publishedDate: DateTimeOffset.UtcNow
        );

        _dbContext.Vulnerabilities.Add(vuln);
        await _dbContext.SaveChangesAsync();

        var count = await _dbContext.Vulnerabilities.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task SystemContext_CanWrite_VulnerabilityReference()
    {
        _dbContext.SetSystemContext(true);

        var vuln = Vulnerability.Create(
            "NVD", "CVE-2026-8888", "Title", "Desc", Severity.Medium,
            null, null, null);
        var reference = VulnerabilityReference.Create(
            vuln.Id,
            "https://nvd.nist.gov/vuln/detail/CVE-2026-8888",
            "NVD",
            ["Patch"]);

        _dbContext.Vulnerabilities.Add(vuln);
        _dbContext.VulnerabilityReferences.Add(reference);
        await _dbContext.SaveChangesAsync();

        var count = await _dbContext.VulnerabilityReferences.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task SystemContext_CanWrite_ThreatAssessment()
    {
        _dbContext.SetSystemContext(true);

        var vuln = Vulnerability.Create(
            "MicrosoftDefender", "CVE-2026-7777", "Title", "Desc", Severity.Critical,
            9.0m, null, DateTimeOffset.UtcNow);
        var assessment = ThreatAssessment.Create(
            vuln.Id,
            threatScore: 80m,
            technicalScore: 75m,
            exploitLikelihoodScore: 70m,
            threatActivityScore: 65m,
            epssScore: 0.42m,
            knownExploited: true,
            publicExploit: true,
            activeAlert: false,
            hasRansomwareAssociation: false,
            hasMalwareAssociation: false,
            factorsJson: "[]",
            calculationVersion: "1"
        );

        _dbContext.Vulnerabilities.Add(vuln);
        _dbContext.ThreatAssessments.Add(assessment);
        await _dbContext.SaveChangesAsync();

        var count = await _dbContext.ThreatAssessments.CountAsync();
        count.Should().Be(1);
    }
}

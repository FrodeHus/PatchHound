using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Assets;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class BusinessLabelsControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly BusinessLabelsController _controller;

    public BusinessLabelsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(_tenantContext));
        _controller = new BusinessLabelsController(_dbContext, _tenantContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // ── Defaults ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_DefaultsToNormalWeightCategory()
    {
        var request = new SaveBusinessLabelRequest("Production", null, null);

        var result = await _controller.Create(request, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<BusinessLabelDto>().Subject;
        dto.WeightCategory.Should().Be("Normal");
        dto.RiskWeight.Should().Be(1.0m);
    }

    [Fact]
    public async Task Create_PersistsWeightCategory()
    {
        var request = new SaveBusinessLabelRequest("Finance", null, null, true, "Critical");

        var result = await _controller.Create(request, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<BusinessLabelDto>().Subject;
        dto.WeightCategory.Should().Be("Critical");
        dto.RiskWeight.Should().Be(2.0m);

        var saved = await _dbContext.BusinessLabels.SingleAsync();
        saved.WeightCategory.Should().Be(BusinessLabelWeightCategory.Critical);
    }

    [Fact]
    public async Task List_IncludesWeightCategoryAndRiskWeight()
    {
        var label = BusinessLabel.Create(_tenantId, "HR", null, null, BusinessLabelWeightCategory.Sensitive);
        await _dbContext.BusinessLabels.AddAsync(label);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.List(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<IReadOnlyList<BusinessLabelDto>>().Subject;
        dtos.Should().ContainSingle();
        dtos[0].WeightCategory.Should().Be("Sensitive");
        dtos[0].RiskWeight.Should().Be(1.5m);
    }

    [Fact]
    public async Task Update_ChangesWeightCategory()
    {
        var label = BusinessLabel.Create(_tenantId, "Shared", null, null, BusinessLabelWeightCategory.Normal);
        await _dbContext.BusinessLabels.AddAsync(label);
        await _dbContext.SaveChangesAsync();

        var request = new SaveBusinessLabelRequest("Shared", null, null, true, "Informational");
        var result = await _controller.Update(label.Id, request, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<BusinessLabelDto>().Subject;
        dto.WeightCategory.Should().Be("Informational");
        dto.RiskWeight.Should().Be(0.5m);
    }

    // ── Validation ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_RejectsInvalidWeightCategory()
    {
        var request = new SaveBusinessLabelRequest("Bad", null, null, true, "NotARealCategory");

        var result = await _controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Update_RejectsInvalidWeightCategory()
    {
        var label = BusinessLabel.Create(_tenantId, "Existing", null, null);
        await _dbContext.BusinessLabels.AddAsync(label);
        await _dbContext.SaveChangesAsync();

        var request = new SaveBusinessLabelRequest("Existing", null, null, true, "NotARealCategory");
        var result = await _controller.Update(label.Id, request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_DefaultsToNormalWhenWeightCategoryIsNull()
    {
        var request = new SaveBusinessLabelRequest("OmittedCategory", null, null, true, null);

        var result = await _controller.Create(request, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<BusinessLabelDto>().Subject;
        dto.WeightCategory.Should().Be("Normal");
    }

    // ── RiskWeight derivation ───────────────────────────────────────────────────

    [Theory]
    [InlineData(BusinessLabelWeightCategory.Informational, 0.5)]
    [InlineData(BusinessLabelWeightCategory.Normal, 1.0)]
    [InlineData(BusinessLabelWeightCategory.Sensitive, 1.5)]
    [InlineData(BusinessLabelWeightCategory.Critical, 2.0)]
    public void BusinessLabel_RiskWeight_DerivedFromCategory(BusinessLabelWeightCategory category, double expectedWeight)
    {
        var label = BusinessLabel.Create(Guid.NewGuid(), "Test", null, null, category);
        label.RiskWeight.Should().Be((decimal)expectedWeight);
    }
}

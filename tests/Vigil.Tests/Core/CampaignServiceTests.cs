using FluentAssertions;
using NSubstitute;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Core.Services;

namespace Vigil.Tests.Core;

public class CampaignServiceTests
{
    private readonly ICampaignRepository _campaignRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CampaignService _service;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public CampaignServiceTests()
    {
        _campaignRepo = Substitute.For<ICampaignRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _service = new CampaignService(_campaignRepo, _unitOfWork);
    }

    private Campaign CreateCampaign(CampaignStatus status = CampaignStatus.Active)
    {
        var campaign = Campaign.Create(_tenantId, "Test Campaign", _userId, "A test campaign");
        if (status == CampaignStatus.Closed)
            campaign.Close();
        return campaign;
    }

    [Fact]
    public async Task CreateAsync_ReturnsCampaign()
    {
        var result = await _service.CreateAsync(
            _tenantId,
            _userId,
            "New Campaign",
            "Description",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("New Campaign");
        result.Value.Description.Should().Be("Description");
        result.Value.TenantId.Should().Be(_tenantId);
        result.Value.CreatedBy.Should().Be(_userId);
        result.Value.Status.Should().Be(CampaignStatus.Active);
        await _campaignRepo.Received(1).AddAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_UpdatesNameAndDescription()
    {
        var campaign = CreateCampaign();
        _campaignRepo.GetByIdAsync(campaign.Id, Arg.Any<CancellationToken>()).Returns(campaign);

        var result = await _service.UpdateAsync(
            campaign.Id,
            "Updated Name",
            "Updated Description",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Updated Name");
        result.Value.Description.Should().Be("Updated Description");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_CampaignNotFound_ReturnsFailure()
    {
        _campaignRepo
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Campaign?)null);

        var result = await _service.UpdateAsync(
            Guid.NewGuid(),
            "Name",
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateAsync_ClosedCampaign_ReturnsFailure()
    {
        var campaign = CreateCampaign(CampaignStatus.Closed);
        _campaignRepo.GetByIdAsync(campaign.Id, Arg.Any<CancellationToken>()).Returns(campaign);

        var result = await _service.UpdateAsync(
            campaign.Id,
            "New Name",
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("closed");
    }

    [Fact]
    public async Task CloseAsync_ClosesCampaign()
    {
        var campaign = CreateCampaign();
        _campaignRepo.GetByIdAsync(campaign.Id, Arg.Any<CancellationToken>()).Returns(campaign);

        var result = await _service.CloseAsync(campaign.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CampaignStatus.Closed);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CloseAsync_CampaignNotFound_ReturnsFailure()
    {
        _campaignRepo
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Campaign?)null);

        var result = await _service.CloseAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task LinkVulnerabilitiesAsync_AddsVulnerabilities()
    {
        var campaign = CreateCampaign();
        _campaignRepo.GetByIdAsync(campaign.Id, Arg.Any<CancellationToken>()).Returns(campaign);
        var vulnIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var result = await _service.LinkVulnerabilitiesAsync(
            campaign.Id,
            vulnIds,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Vulnerabilities.Should().HaveCount(2);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkVulnerabilitiesAsync_CampaignNotFound_ReturnsFailure()
    {
        _campaignRepo
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Campaign?)null);

        var result = await _service.LinkVulnerabilitiesAsync(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task LinkVulnerabilitiesAsync_ClosedCampaign_ReturnsFailure()
    {
        var campaign = CreateCampaign(CampaignStatus.Closed);
        _campaignRepo.GetByIdAsync(campaign.Id, Arg.Any<CancellationToken>()).Returns(campaign);

        var result = await _service.LinkVulnerabilitiesAsync(
            campaign.Id,
            [Guid.NewGuid()],
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("closed");
    }
}

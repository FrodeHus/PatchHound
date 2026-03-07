using FluentAssertions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;

namespace PatchHound.Tests.Core;

public class RiskAcceptanceServiceTests
{
    private readonly IRiskAcceptanceRepository _riskAcceptanceRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly RiskAcceptanceService _service;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _requestedBy = Guid.NewGuid();
    private readonly Guid _vulnerabilityId = Guid.NewGuid();

    public RiskAcceptanceServiceTests()
    {
        _riskAcceptanceRepo = Substitute.For<IRiskAcceptanceRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _service = new RiskAcceptanceService(_riskAcceptanceRepo, _unitOfWork);
    }

    [Fact]
    public async Task RequestAsync_Creates_Pending_Record()
    {
        var result = await _service.RequestAsync(
            _vulnerabilityId,
            _tenantId,
            _requestedBy,
            "Risk is acceptable",
            null,
            null,
            null,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RiskAcceptanceStatus.Pending);
        result.Value.VulnerabilityId.Should().Be(_vulnerabilityId);
        result.Value.TenantId.Should().Be(_tenantId);
        result.Value.RequestedBy.Should().Be(_requestedBy);
        result.Value.Justification.Should().Be("Risk is acceptable");

        await _riskAcceptanceRepo
            .Received(1)
            .AddAsync(Arg.Any<RiskAcceptance>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestAsync_Without_Justification_Fails()
    {
        var result = await _service.RequestAsync(
            _vulnerabilityId,
            _tenantId,
            _requestedBy,
            "",
            null,
            null,
            null,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Justification is required");
    }

    [Fact]
    public async Task RequestAsync_With_Null_Justification_Fails()
    {
        var result = await _service.RequestAsync(
            _vulnerabilityId,
            _tenantId,
            _requestedBy,
            null!,
            null,
            null,
            null,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Justification is required");
    }

    [Fact]
    public async Task ApproveAsync_Transitions_To_Approved()
    {
        var acceptance = RiskAcceptance.Create(
            _vulnerabilityId,
            _tenantId,
            _requestedBy,
            "Justified"
        );
        var approvedBy = Guid.NewGuid();
        _riskAcceptanceRepo
            .GetByIdAsync(acceptance.Id, Arg.Any<CancellationToken>())
            .Returns(acceptance);

        var result = await _service.ApproveAsync(
            acceptance.Id,
            approvedBy,
            "With conditions",
            DateTimeOffset.UtcNow.AddMonths(6),
            90,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RiskAcceptanceStatus.Approved);
        result.Value.ApprovedBy.Should().Be(approvedBy);
        result.Value.ApprovedAt.Should().NotBeNull();
        result.Value.Conditions.Should().Be("With conditions");
        result.Value.ExpiryDate.Should().NotBeNull();
        result.Value.ReviewFrequency.Should().Be(90);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectAsync_Transitions_To_Rejected()
    {
        var acceptance = RiskAcceptance.Create(
            _vulnerabilityId,
            _tenantId,
            _requestedBy,
            "Justified"
        );
        var rejectedBy = Guid.NewGuid();
        _riskAcceptanceRepo
            .GetByIdAsync(acceptance.Id, Arg.Any<CancellationToken>())
            .Returns(acceptance);

        var result = await _service.RejectAsync(acceptance.Id, rejectedBy, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RiskAcceptanceStatus.Rejected);
        result.Value.ApprovedBy.Should().Be(rejectedBy);
        result.Value.ApprovedAt.Should().NotBeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAsync_NonPending_Fails()
    {
        var acceptance = RiskAcceptance.Create(
            _vulnerabilityId,
            _tenantId,
            _requestedBy,
            "Justified"
        );
        acceptance.Approve(Guid.NewGuid()); // Already approved
        _riskAcceptanceRepo
            .GetByIdAsync(acceptance.Id, Arg.Any<CancellationToken>())
            .Returns(acceptance);

        var result = await _service.ApproveAsync(
            acceptance.Id,
            Guid.NewGuid(),
            null,
            null,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Only pending");
    }

    [Fact]
    public async Task RejectAsync_NonPending_Fails()
    {
        var acceptance = RiskAcceptance.Create(
            _vulnerabilityId,
            _tenantId,
            _requestedBy,
            "Justified"
        );
        acceptance.Reject(Guid.NewGuid()); // Already rejected
        _riskAcceptanceRepo
            .GetByIdAsync(acceptance.Id, Arg.Any<CancellationToken>())
            .Returns(acceptance);

        var result = await _service.RejectAsync(
            acceptance.Id,
            Guid.NewGuid(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Only pending");
    }

    [Fact]
    public async Task ApproveAsync_NotFound_ReturnsFailure()
    {
        _riskAcceptanceRepo
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RiskAcceptance?)null);

        var result = await _service.ApproveAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RejectAsync_NotFound_ReturnsFailure()
    {
        _riskAcceptanceRepo
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RiskAcceptance?)null);

        var result = await _service.RejectAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RequestAsync_With_Optional_Fields_Succeeds()
    {
        var assetId = Guid.NewGuid();
        var expiryDate = DateTimeOffset.UtcNow.AddMonths(6);

        var result = await _service.RequestAsync(
            _vulnerabilityId,
            _tenantId,
            _requestedBy,
            "Risk is acceptable",
            assetId,
            "Must monitor weekly",
            expiryDate,
            30,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.AssetId.Should().Be(assetId);
        result.Value.Conditions.Should().Be("Must monitor weekly");
        result.Value.ExpiryDate.Should().Be(expiryDate);
        result.Value.ReviewFrequency.Should().Be(30);
    }
}

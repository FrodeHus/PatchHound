using FluentAssertions;
using NSubstitute;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Core.Services;

namespace Vigil.Tests.Core;

public class AssetServiceTests
{
    private readonly IAssetRepository _assetRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AssetService _service;
    private readonly Guid _tenantId = Guid.NewGuid();

    public AssetServiceTests()
    {
        _assetRepo = Substitute.For<IAssetRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _service = new AssetService(_assetRepo, _unitOfWork);
    }

    private Asset CreateAsset(Criticality criticality = Criticality.Medium)
    {
        return Asset.Create(_tenantId, "EXT-001", AssetType.Device, "Test Server", criticality);
    }

    [Fact]
    public async Task AssignOwner_SetsUserOwner_Successfully()
    {
        var asset = CreateAsset();
        var userId = Guid.NewGuid();
        _assetRepo.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(asset);

        var result = await _service.AssignOwnerAsync(asset.Id, userId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.OwnerType.Should().Be(OwnerType.User);
        result.Value.OwnerUserId.Should().Be(userId);
        result.Value.OwnerTeamId.Should().BeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignTeamOwner_SetsTeamOwner_Successfully()
    {
        var asset = CreateAsset();
        var teamId = Guid.NewGuid();
        _assetRepo.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(asset);

        var result = await _service.AssignTeamOwnerAsync(asset.Id, teamId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.OwnerType.Should().Be(OwnerType.Team);
        result.Value.OwnerTeamId.Should().Be(teamId);
        result.Value.OwnerUserId.Should().BeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCriticality_UpdatesCriticality_Successfully()
    {
        var asset = CreateAsset(Criticality.Low);
        _assetRepo.GetByIdAsync(asset.Id, Arg.Any<CancellationToken>()).Returns(asset);

        var result = await _service.SetCriticalityAsync(asset.Id, Criticality.Critical, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Criticality.Should().Be(Criticality.Critical);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignOwner_AssetNotFound_ReturnsFailure()
    {
        _assetRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Asset?)null);

        var result = await _service.AssignOwnerAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task AssignTeamOwner_AssetNotFound_ReturnsFailure()
    {
        _assetRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Asset?)null);

        var result = await _service.AssignTeamOwnerAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task SetCriticality_AssetNotFound_ReturnsFailure()
    {
        _assetRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Asset?)null);

        var result = await _service.SetCriticalityAsync(Guid.NewGuid(), Criticality.High, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task BulkAssignOwner_AssignsToMultipleAssets()
    {
        var asset1 = CreateAsset();
        var asset2 = CreateAsset();
        var userId = Guid.NewGuid();
        var assetIds = new List<Guid> { asset1.Id, asset2.Id };

        _assetRepo.GetByIdAsync(asset1.Id, Arg.Any<CancellationToken>()).Returns(asset1);
        _assetRepo.GetByIdAsync(asset2.Id, Arg.Any<CancellationToken>()).Returns(asset2);

        var result = await _service.BulkAssignOwnerAsync(assetIds, userId, OwnerType.User, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
        asset1.OwnerUserId.Should().Be(userId);
        asset2.OwnerUserId.Should().Be(userId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BulkAssignOwner_WithTeamType_AssignsTeamOwner()
    {
        var asset1 = CreateAsset();
        var teamId = Guid.NewGuid();
        var assetIds = new List<Guid> { asset1.Id };

        _assetRepo.GetByIdAsync(asset1.Id, Arg.Any<CancellationToken>()).Returns(asset1);

        var result = await _service.BulkAssignOwnerAsync(assetIds, teamId, OwnerType.Team, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        asset1.OwnerType.Should().Be(OwnerType.Team);
        asset1.OwnerTeamId.Should().Be(teamId);
        asset1.OwnerUserId.Should().BeNull();
    }

    [Fact]
    public async Task BulkAssignOwner_SkipsMissingAssets_ReturnsUpdatedCount()
    {
        var asset1 = CreateAsset();
        var missingId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assetIds = new List<Guid> { asset1.Id, missingId };

        _assetRepo.GetByIdAsync(asset1.Id, Arg.Any<CancellationToken>()).Returns(asset1);
        _assetRepo.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Asset?)null);

        var result = await _service.BulkAssignOwnerAsync(assetIds, userId, OwnerType.User, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        asset1.OwnerUserId.Should().Be(userId);
    }
}

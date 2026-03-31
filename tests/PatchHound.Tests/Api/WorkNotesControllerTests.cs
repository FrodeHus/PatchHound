using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.WorkNotes;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class WorkNotesControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _authorId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly WorkNotesController _controller;

    public WorkNotesControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.CurrentUserId.Returns(_authorId);
        _tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
        _controller = new WorkNotesController(_dbContext, _tenantContext);
    }

    [Fact]
    public async Task Create_AssetWorkNote_StoresAssetEntityType()
    {
        var asset = await SeedAssetAsync();
        await SeedUsersAsync();

        var action = await _controller.Create(
            "assets",
            asset.Id,
            new CreateWorkNoteRequest("Needs owner follow-up"),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<CreatedResult>().Subject;
        var dto = result.Value.Should().BeOfType<WorkNoteDto>().Subject;

        dto.EntityType.Should().Be(nameof(Asset));
        dto.EntityId.Should().Be(asset.Id);
        dto.AuthorId.Should().Be(_authorId);
        dto.CanEdit.Should().BeTrue();
        dto.CanDelete.Should().BeTrue();

        var stored = await _dbContext.Comments.SingleAsync();
        stored.EntityType.Should().Be(nameof(Asset));
        stored.EntityId.Should().Be(asset.Id);
        stored.Content.Should().Be("Needs owner follow-up");
        stored.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task List_ExcludesDeletedNotes()
    {
        var asset = await SeedAssetAsync();
        await SeedUsersAsync();

        var visibleNote = Comment.Create(
            _tenantId,
            nameof(Asset),
            asset.Id,
            _authorId,
            "Visible note"
        );
        var deletedNote = Comment.Create(
            _tenantId,
            nameof(Asset),
            asset.Id,
            _authorId,
            "Deleted note"
        );
        deletedNote.Delete();

        await _dbContext.Comments.AddRangeAsync(visibleNote, deletedNote);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List("assets", asset.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var items = result.Value.Should().BeOfType<List<WorkNoteDto>>().Subject;

        items.Should().ContainSingle();
        items[0].Content.Should().Be("Visible note");
    }

    [Fact]
    public async Task Update_WhenCurrentUserIsNotAuthor_ReturnsForbid()
    {
        var note = await SeedAuthorNoteAsync();
        _tenantContext.CurrentUserId.Returns(_otherUserId);

        var action = await _controller.Update(
            note.Id,
            new UpdateWorkNoteRequest("Attempted edit"),
            CancellationToken.None
        );

        action.Result.Should().BeOfType<ForbidResult>();

        var stored = await _dbContext.Comments.SingleAsync();
        stored.Content.Should().Be("Original note");
    }

    [Fact]
    public async Task Delete_WhenCurrentUserIsAuthor_SoftDeletesNote()
    {
        var note = await SeedAuthorNoteAsync();

        var action = await _controller.Delete(note.Id, CancellationToken.None);

        action.Should().BeOfType<NoContentResult>();

        var stored = await _dbContext.Comments.SingleAsync();
        stored.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_WhenCurrentUserIsNotAuthor_ReturnsForbid()
    {
        var note = await SeedAuthorNoteAsync();
        _tenantContext.CurrentUserId.Returns(_otherUserId);

        var action = await _controller.Delete(note.Id, CancellationToken.None);

        action.Should().BeOfType<ForbidResult>();

        var stored = await _dbContext.Comments.SingleAsync();
        stored.DeletedAt.Should().BeNull();
    }

    private async Task SeedUsersAsync()
    {
        await _dbContext.Users.AddRangeAsync(
            User.Create("author@example.com", "Author User", _authorId.ToString()),
            User.Create("other@example.com", "Other User", _otherUserId.ToString())
        );
        await _dbContext.SaveChangesAsync();
    }

    private async Task<Asset> SeedAssetAsync()
    {
        var asset = Asset.Create(
            _tenantId,
            "device-001",
            AssetType.Device,
            "Workstation 01",
            Criticality.Medium
        );

        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();
        return asset;
    }

    private async Task<Comment> SeedAuthorNoteAsync()
    {
        var asset = await SeedAssetAsync();
        await SeedUsersAsync();

        var note = Comment.Create(
            _tenantId,
            nameof(Asset),
            asset.Id,
            _authorId,
            "Original note"
        );

        await _dbContext.Comments.AddAsync(note);
        await _dbContext.SaveChangesAsync();
        return note;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}

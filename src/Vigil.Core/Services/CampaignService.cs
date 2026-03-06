using Vigil.Core.Common;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;

namespace Vigil.Core.Services;

public class CampaignService
{
    private readonly ICampaignRepository _campaignRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CampaignService(ICampaignRepository campaignRepository, IUnitOfWork unitOfWork)
    {
        _campaignRepository = campaignRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Campaign>> CreateAsync(
        Guid tenantId,
        Guid createdBy,
        string name,
        string? description,
        CancellationToken ct)
    {
        var campaign = Campaign.Create(tenantId, name, createdBy, description);
        await _campaignRepository.AddAsync(campaign, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<Campaign>.Success(campaign);
    }

    public async Task<Result<Campaign>> UpdateAsync(
        Guid campaignId,
        string? name,
        string? description,
        CancellationToken ct)
    {
        var campaign = await _campaignRepository.GetByIdAsync(campaignId, ct);
        if (campaign is null)
            return Result<Campaign>.Failure("Campaign not found");

        if (campaign.Status == CampaignStatus.Closed)
            return Result<Campaign>.Failure("Cannot modify a closed campaign");

        campaign.Update(name, description);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<Campaign>.Success(campaign);
    }

    public async Task<Result<Campaign>> CloseAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await _campaignRepository.GetByIdAsync(campaignId, ct);
        if (campaign is null)
            return Result<Campaign>.Failure("Campaign not found");

        campaign.Close();
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<Campaign>.Success(campaign);
    }

    public async Task<Result<Campaign>> LinkVulnerabilitiesAsync(
        Guid campaignId,
        IReadOnlyList<Guid> vulnerabilityIds,
        CancellationToken ct)
    {
        var campaign = await _campaignRepository.GetByIdAsync(campaignId, ct);
        if (campaign is null)
            return Result<Campaign>.Failure("Campaign not found");

        if (campaign.Status == CampaignStatus.Closed)
            return Result<Campaign>.Failure("Cannot modify a closed campaign");

        foreach (var vulnId in vulnerabilityIds)
            campaign.AddVulnerability(vulnId);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result<Campaign>.Success(campaign);
    }
}

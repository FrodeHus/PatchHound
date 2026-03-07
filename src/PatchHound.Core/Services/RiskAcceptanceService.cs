using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;

namespace PatchHound.Core.Services;

public class RiskAcceptanceService
{
    private readonly IRiskAcceptanceRepository _riskAcceptanceRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RiskAcceptanceService(
        IRiskAcceptanceRepository riskAcceptanceRepository,
        IUnitOfWork unitOfWork
    )
    {
        _riskAcceptanceRepository = riskAcceptanceRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RiskAcceptance>> RequestAsync(
        Guid vulnerabilityId,
        Guid tenantId,
        Guid requestedBy,
        string justification,
        Guid? assetId,
        string? conditions,
        DateTimeOffset? expiryDate,
        int? reviewFrequency,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(justification))
            return Result<RiskAcceptance>.Failure("Justification is required");

        var acceptance = RiskAcceptance.Create(
            vulnerabilityId,
            tenantId,
            requestedBy,
            justification,
            assetId,
            conditions,
            expiryDate,
            reviewFrequency
        );

        await _riskAcceptanceRepository.AddAsync(acceptance, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<RiskAcceptance>.Success(acceptance);
    }

    public async Task<Result<RiskAcceptance>> ApproveAsync(
        Guid riskAcceptanceId,
        Guid approvedBy,
        string? conditions,
        DateTimeOffset? expiryDate,
        int? reviewFrequency,
        CancellationToken ct
    )
    {
        var acceptance = await _riskAcceptanceRepository.GetByIdAsync(riskAcceptanceId, ct);
        if (acceptance is null)
            return Result<RiskAcceptance>.Failure("Risk acceptance not found");

        if (acceptance.Status != RiskAcceptanceStatus.Pending)
            return Result<RiskAcceptance>.Failure("Only pending risk acceptances can be approved");

        acceptance.Approve(approvedBy, conditions, expiryDate, reviewFrequency);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<RiskAcceptance>.Success(acceptance);
    }

    public async Task<Result<RiskAcceptance>> RejectAsync(
        Guid riskAcceptanceId,
        Guid rejectedBy,
        CancellationToken ct
    )
    {
        var acceptance = await _riskAcceptanceRepository.GetByIdAsync(riskAcceptanceId, ct);
        if (acceptance is null)
            return Result<RiskAcceptance>.Failure("Risk acceptance not found");

        if (acceptance.Status != RiskAcceptanceStatus.Pending)
            return Result<RiskAcceptance>.Failure("Only pending risk acceptances can be rejected");

        acceptance.Reject(rejectedBy);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<RiskAcceptance>.Success(acceptance);
    }
}

using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;

namespace PatchHound.Api.Services;

public sealed class DeviceRulePreviewService
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly IDeviceRuleEvaluationService _evaluationService;
    private readonly SoftwareRuleFilterBuilder _softwareFilterBuilder;
    private readonly CloudApplicationRuleFilterBuilder _cloudApplicationFilterBuilder;

    public DeviceRulePreviewService(
        PatchHoundDbContext dbContext,
        IDeviceRuleEvaluationService evaluationService,
        SoftwareRuleFilterBuilder softwareFilterBuilder,
        CloudApplicationRuleFilterBuilder cloudApplicationFilterBuilder)
    {
        _dbContext = dbContext;
        _evaluationService = evaluationService;
        _softwareFilterBuilder = softwareFilterBuilder;
        _cloudApplicationFilterBuilder = cloudApplicationFilterBuilder;
    }

    public async Task<DeviceRulePreviewResult> PreviewAsync(
        Guid tenantId,
        string assetType,
        FilterNode filter,
        CancellationToken ct)
    {
        if (DeviceRuleAssetTypes.IsDevice(assetType))
            return await _evaluationService.PreviewFilterAsync(tenantId, filter, ct);

        if (DeviceRuleAssetTypes.IsApplication(assetType))
            return await PreviewApplicationFilterAsync(tenantId, filter, ct);

        if (DeviceRuleAssetTypes.IsSoftware(assetType))
            return await PreviewSoftwareFilterAsync(tenantId, filter, ct);

        throw new InvalidOperationException("Unsupported asset type.");
    }

    private async Task<DeviceRulePreviewResult> PreviewSoftwareFilterAsync(
        Guid tenantId,
        FilterNode filter,
        CancellationToken ct)
    {
        var predicate = _softwareFilterBuilder.Build(filter);
        var query = _dbContext.SoftwareTenantRecords
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Where(predicate);

        var count = await query.CountAsync(ct);
        var samples = await query
            .OrderBy(item => item.SoftwareProduct.Name)
            .ThenBy(item => item.SoftwareProduct.Vendor)
            .Take(5)
            .Select(item => new DevicePreviewItem(
                item.Id,
                string.IsNullOrWhiteSpace(item.SoftwareProduct.Vendor)
                    ? item.SoftwareProduct.Name
                    : $"{item.SoftwareProduct.Vendor} {item.SoftwareProduct.Name}"))
            .ToListAsync(ct);

        return new DeviceRulePreviewResult(count, samples);
    }

    private async Task<DeviceRulePreviewResult> PreviewApplicationFilterAsync(
        Guid tenantId,
        FilterNode filter,
        CancellationToken ct)
    {
        var predicate = _cloudApplicationFilterBuilder.Build(filter);
        var query = _dbContext.CloudApplications
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.ActiveInTenant)
            .Where(predicate);

        var count = await query.CountAsync(ct);
        var samples = await query
            .OrderBy(item => item.Name)
            .Take(5)
            .Select(item => new DevicePreviewItem(item.Id, item.Name))
            .ToListAsync(ct);

        return new DeviceRulePreviewResult(count, samples);
    }
}

using System.Linq.Expressions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class AssetRuleFilterBuilder
{
    private readonly PatchHoundDbContext _dbContext;

    public AssetRuleFilterBuilder(PatchHoundDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Expression<Func<Asset, bool>> Build(FilterNode root)
    {
        var param = Expression.Parameter(typeof(Asset), "a");
        var body = BuildNode(root, param);
        return Expression.Lambda<Func<Asset, bool>>(body, param);
    }

    private Expression BuildNode(FilterNode node, ParameterExpression param)
    {
        return node switch
        {
            FilterGroup group => BuildGroup(group, param),
            FilterCondition condition => BuildCondition(condition, param),
            _ => throw new InvalidOperationException($"Unknown filter node type: {node.Type}")
        };
    }

    private Expression BuildGroup(FilterGroup group, ParameterExpression param)
    {
        if (group.Conditions.Count == 0)
            return Expression.Constant(true);

        var expressions = group.Conditions.Select(c => BuildNode(c, param)).ToList();
        var combined = expressions[0];
        for (var i = 1; i < expressions.Count; i++)
        {
            combined = group.Operator.Equals("OR", StringComparison.OrdinalIgnoreCase)
                ? Expression.OrElse(combined, expressions[i])
                : Expression.AndAlso(combined, expressions[i]);
        }
        return combined;
    }

    private Expression BuildCondition(FilterCondition condition, ParameterExpression param)
    {
        return condition.Field switch
        {
            "AssetType" => BuildAssetTypeComparison(param, condition),
            "Name" => BuildStringComparison(param, nameof(Asset.Name), condition),
            "DeviceGroup" => BuildNullableStringComparison(param, nameof(Asset.DeviceGroupName), condition),
            "Platform" => BuildNullableStringComparison(param, nameof(Asset.DeviceOsPlatform), condition),
            "Domain" => BuildNullableStringComparison(param, nameof(Asset.DeviceComputerDnsName), condition),
            "Tag" => BuildTagCondition(param, condition),
            _ => throw new InvalidOperationException($"Unknown filter field: {condition.Field}")
        };
    }

    private static Expression BuildAssetTypeComparison(ParameterExpression param, FilterCondition condition)
    {
        if (!Enum.TryParse<AssetType>(condition.Value, ignoreCase: true, out var enumValue))
            throw new InvalidOperationException($"Invalid AssetType value: {condition.Value}");

        var property = Expression.Property(param, nameof(Asset.AssetType));
        return Expression.Equal(property, Expression.Constant(enumValue));
    }

    private static Expression BuildStringComparison(
        ParameterExpression param, string propertyName, FilterCondition condition)
    {
        var property = Expression.Property(param, propertyName);
        var value = Expression.Constant(condition.Value);
        return BuildStringOp(property, value, condition.Op);
    }

    private static Expression BuildNullableStringComparison(
        ParameterExpression param, string propertyName, FilterCondition condition)
    {
        var property = Expression.Property(param, propertyName);
        var value = Expression.Constant(condition.Value);
        var nullCheck = Expression.NotEqual(property, Expression.Constant(null, typeof(string)));
        var comparison = BuildStringOp(property, value, condition.Op);
        return Expression.AndAlso(nullCheck, comparison);
    }

    private static Expression BuildStringOp(Expression property, Expression value, string op)
    {
        // EF Core translates these to SQL LIKE/= with appropriate patterns.
        // StringComparison is not used here because EF Core PostgreSQL provider
        // uses database collation for case sensitivity. Use EF.Functions.ILike
        // if case-insensitive matching is needed on a case-sensitive collation.
        return op switch
        {
            "Equals" => Expression.Call(property,
                typeof(string).GetMethod(nameof(string.Equals), [typeof(string)])!, value),
            "StartsWith" => Expression.Call(property,
                typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!, value),
            "Contains" => Expression.Call(property,
                typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!, value),
            "EndsWith" => Expression.Call(property,
                typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!, value),
            _ => throw new InvalidOperationException($"Unknown operator: {op}")
        };
    }

    private Expression BuildTagCondition(ParameterExpression param, FilterCondition condition)
    {
        // Builds: _dbContext.AssetTags.Any(t => t.AssetId == a.Id && <string op on t.Tag>)
        var assetId = Expression.Property(param, nameof(Asset.Id));
        var tagParam = Expression.Parameter(typeof(AssetTag), "t");
        var tagAssetId = Expression.Property(tagParam, nameof(AssetTag.AssetId));
        var tagValue = Expression.Property(tagParam, nameof(AssetTag.Tag));
        var value = Expression.Constant(condition.Value);

        var assetIdMatch = Expression.Equal(tagAssetId, assetId);
        var stringMatch = BuildStringOp(tagValue, value, condition.Op);
        var predicate = Expression.AndAlso(assetIdMatch, stringMatch);
        var lambda = Expression.Lambda<Func<AssetTag, bool>>(predicate, tagParam);

        var assetTags = Expression.Property(Expression.Constant(_dbContext), nameof(PatchHoundDbContext.AssetTags));
        var anyMethod = typeof(Queryable).GetMethods()
            .First(m => m.Name == "Any" && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(AssetTag));
        return Expression.Call(anyMethod, assetTags, lambda);
    }
}

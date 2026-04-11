using System.Linq.Expressions;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.Inventory;

public class DeviceRuleFilterBuilder
{
    private readonly PatchHoundDbContext _dbContext;

    public DeviceRuleFilterBuilder(PatchHoundDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Expression<Func<Device, bool>> Build(FilterNode root)
    {
        var param = Expression.Parameter(typeof(Device), "d");
        var body = BuildNode(root, param);
        return Expression.Lambda<Func<Device, bool>>(body, param);
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
            "Name" => BuildStringComparison(param, nameof(Device.Name), condition),
            "DeviceGroup" => BuildNullableStringComparison(param, nameof(Device.GroupName), condition),
            "Platform" => BuildNullableStringComparison(param, nameof(Device.OsPlatform), condition),
            "Domain" => BuildNullableStringComparison(param, nameof(Device.ComputerDnsName), condition),
            "Tag" => BuildTagCondition(param, condition),
            _ => throw new InvalidOperationException($"Unknown filter field: {condition.Field}")
        };
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
        // Builds: _dbContext.DeviceTags.Any(t => t.DeviceId == d.Id && <string op on t.Value>)
        // DeviceTag is a structured Key/Value pair — the rule filter matches against the
        // Value payload to preserve the semantics of the legacy flat AssetTag.Tag column.
        var deviceId = Expression.Property(param, nameof(Device.Id));
        var tagParam = Expression.Parameter(typeof(DeviceTag), "t");
        var tagDeviceId = Expression.Property(tagParam, nameof(DeviceTag.DeviceId));
        var tagValue = Expression.Property(tagParam, nameof(DeviceTag.Value));
        var value = Expression.Constant(condition.Value);

        var deviceIdMatch = Expression.Equal(tagDeviceId, deviceId);
        var stringMatch = BuildStringOp(tagValue, value, condition.Op);
        var predicate = Expression.AndAlso(deviceIdMatch, stringMatch);
        var lambda = Expression.Lambda<Func<DeviceTag, bool>>(predicate, tagParam);

        var deviceTags = Expression.Property(
            Expression.Constant(_dbContext),
            nameof(PatchHoundDbContext.DeviceTags));
        var anyMethod = typeof(Queryable).GetMethods()
            .First(m => m.Name == "Any" && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(DeviceTag));
        return Expression.Call(anyMethod, deviceTags, lambda);
    }
}

using System.Linq.Expressions;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services.Inventory;

public class SoftwareRuleFilterBuilder
{
    public Expression<Func<SoftwareTenantRecord, bool>> Build(FilterNode root)
    {
        var parameter = Expression.Parameter(typeof(SoftwareTenantRecord), "software");
        var body = BuildNode(root, parameter);
        return Expression.Lambda<Func<SoftwareTenantRecord, bool>>(body, parameter);
    }

    private static Expression BuildNode(FilterNode node, ParameterExpression parameter)
    {
        return node switch
        {
            FilterGroup group => BuildGroup(group, parameter),
            FilterCondition condition => BuildCondition(condition, parameter),
            _ => throw new InvalidOperationException($"Unknown filter node type: {node.Type}")
        };
    }

    private static Expression BuildGroup(FilterGroup group, ParameterExpression parameter)
    {
        if (group.Conditions.Count == 0)
        {
            return Expression.Constant(true);
        }

        Expression? combined = null;
        foreach (var child in group.Conditions)
        {
            var childExpression = BuildNode(child, parameter);
            combined = combined is null
                ? childExpression
                : group.Operator.Equals("OR", StringComparison.OrdinalIgnoreCase)
                    ? Expression.OrElse(combined, childExpression)
                    : Expression.AndAlso(combined, childExpression);
        }

        return combined ?? Expression.Constant(true);
    }

    private static Expression BuildCondition(FilterCondition condition, ParameterExpression parameter)
    {
        var value = condition.Value ?? string.Empty;
        Expression property = condition.Field switch
        {
            "Name" => Expression.Property(
                Expression.Property(parameter, nameof(SoftwareTenantRecord.SoftwareProduct)),
                nameof(SoftwareProduct.Name)),
            "Vendor" => Expression.Property(
                Expression.Property(parameter, nameof(SoftwareTenantRecord.SoftwareProduct)),
                nameof(SoftwareProduct.Vendor)),
            _ => throw new InvalidOperationException($"Unknown software filter field: {condition.Field}")
        };

        Expression coalesced = property.Type == typeof(string)
            ? property
            : Expression.Coalesce(property, Expression.Constant(string.Empty));
        var constant = Expression.Constant(value);

        return condition.Op switch
        {
            nameof(string.Equals) => Expression.Equal(coalesced, constant),
            nameof(string.StartsWith) => Expression.Call(
                coalesced,
                typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!,
                constant),
            nameof(string.Contains) => Expression.Call(
                coalesced,
                typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
                constant),
            nameof(string.EndsWith) => Expression.Call(
                coalesced,
                typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!,
                constant),
            _ => throw new InvalidOperationException($"Unknown operator: {condition.Op}")
        };
    }
}

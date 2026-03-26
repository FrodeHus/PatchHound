using System.Linq.Expressions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services;

public class TeamMembershipRuleFilterBuilder
{
    public Expression<Func<User, bool>> Build(Guid tenantId, FilterNode root)
    {
        var parameter = Expression.Parameter(typeof(User), "u");
        var body = BuildNode(root, tenantId, parameter);
        return Expression.Lambda<Func<User, bool>>(body, parameter);
    }

    private Expression BuildNode(FilterNode node, Guid tenantId, ParameterExpression parameter)
    {
        return node switch
        {
            FilterGroup group => BuildGroup(group, tenantId, parameter),
            FilterCondition condition => BuildCondition(condition, tenantId, parameter),
            _ => throw new InvalidOperationException($"Unknown filter node type: {node.Type}")
        };
    }

    private Expression BuildGroup(FilterGroup group, Guid tenantId, ParameterExpression parameter)
    {
        if (group.Conditions.Count == 0)
        {
            return Expression.Constant(true);
        }

        var expressions = group.Conditions.Select(condition => BuildNode(condition, tenantId, parameter)).ToList();
        var combined = expressions[0];
        for (var i = 1; i < expressions.Count; i++)
        {
            combined = group.Operator.Equals("OR", StringComparison.OrdinalIgnoreCase)
                ? Expression.OrElse(combined, expressions[i])
                : Expression.AndAlso(combined, expressions[i]);
        }

        return combined;
    }

    private Expression BuildCondition(FilterCondition condition, Guid tenantId, ParameterExpression parameter)
    {
        return condition.Field switch
        {
            "DisplayName" => BuildStringComparison(parameter, nameof(User.DisplayName), condition),
            "Email" => BuildStringComparison(parameter, nameof(User.Email), condition),
            "Company" => BuildNullableStringComparison(parameter, nameof(User.Company), condition),
            "Role" => BuildRoleCondition(parameter, tenantId, condition),
            _ => throw new InvalidOperationException($"Unknown rule field: {condition.Field}")
        };
    }

    private static Expression BuildStringComparison(
        ParameterExpression parameter,
        string propertyName,
        FilterCondition condition
    )
    {
        var property = Expression.Property(parameter, propertyName);
        return BuildStringOperator(property, Expression.Constant(condition.Value), condition.Op);
    }

    private static Expression BuildNullableStringComparison(
        ParameterExpression parameter,
        string propertyName,
        FilterCondition condition
    )
    {
        var property = Expression.Property(parameter, propertyName);
        var notNull = Expression.NotEqual(property, Expression.Constant(null, typeof(string)));
        var comparison = BuildStringOperator(property, Expression.Constant(condition.Value), condition.Op);
        return Expression.AndAlso(notNull, comparison);
    }

    private static Expression BuildRoleCondition(
        ParameterExpression parameter,
        Guid tenantId,
        FilterCondition condition
    )
    {
        if (!string.Equals(condition.Op, "Equals", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Role rules only support Equals.");
        }

        if (!Enum.TryParse<RoleName>(condition.Value, ignoreCase: true, out var role))
        {
            throw new InvalidOperationException($"Invalid role value: {condition.Value}");
        }

        var roleParameter = Expression.Parameter(typeof(UserTenantRole), "utr");
        var tenantMatch = Expression.Equal(
            Expression.Property(roleParameter, nameof(UserTenantRole.TenantId)),
            Expression.Constant(tenantId)
        );
        var roleMatch = Expression.Equal(
            Expression.Property(roleParameter, nameof(UserTenantRole.Role)),
            Expression.Constant(role)
        );
        var predicate = Expression.Lambda<Func<UserTenantRole, bool>>(
            Expression.AndAlso(tenantMatch, roleMatch),
            roleParameter
        );

        var rolesProperty = Expression.Property(parameter, nameof(User.TenantRoles));
        var anyMethod = typeof(Enumerable).GetMethods()
            .First(method => method.Name == nameof(Enumerable.Any) && method.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(UserTenantRole));

        return Expression.Call(anyMethod, rolesProperty, predicate);
    }

    private static Expression BuildStringOperator(Expression left, Expression right, string op)
    {
        return op switch
        {
            "Equals" => Expression.Call(
                left,
                typeof(string).GetMethod(nameof(string.Equals), [typeof(string)])!,
                right
            ),
            "StartsWith" => Expression.Call(
                left,
                typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!,
                right
            ),
            "Contains" => Expression.Call(
                left,
                typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
                right
            ),
            "EndsWith" => Expression.Call(
                left,
                typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!,
                right
            ),
            _ => throw new InvalidOperationException($"Unknown operator: {op}")
        };
    }
}

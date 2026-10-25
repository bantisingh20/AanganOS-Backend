using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using UrbanNest.Application.Exceptions;

namespace UrbanNest.Application.Querying;

/// <summary>
/// Applies <see cref="ListQueryParameters"/> (filter → search → sort) to an <see cref="IQueryable{T}"/>
/// using only the allow-listed selectors declared on the entity's <see cref="ListQueryConfiguration{T}"/>.
/// Everything it builds is a composed <see cref="Expression"/> over the entity's own properties,
/// so the provider (EF Core) translates it to SQL — nothing is materialized or filtered in memory
/// (Req 9.5, 65.1). Any sort/filter/search target the client names that is NOT in the allow-list
/// is rejected with a <see cref="ValidationException"/>; it is never turned into an arbitrary
/// expression (security — no injection, no over-exposure).
///
/// Paging (clamp + Skip/Take) and async materialization live in the EF-aware extension that calls
/// this; keeping the IQueryable shaping here means it is pure, provider-agnostic and unit-testable.
/// </summary>
public static class ListQueryBuilder
{
    private static readonly MethodInfo StringContains =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    private static readonly MethodInfo StringStartsWith =
        typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;

    /// <summary>Apply filters, then search, then ordering (with deterministic tie-break).</summary>
    public static IQueryable<T> Apply<T>(
        IQueryable<T> source,
        ListQueryParameters parameters,
        ListQueryConfiguration<T> config)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(config);

        source = ApplyFilters(source, parameters.Filters, config);
        source = ApplySearch(source, parameters.Search, config);
        source = ApplyOrdering(source, parameters.Sort, config);
        return source;
    }

    private static IQueryable<T> ApplyFilters<T>(
        IQueryable<T> source,
        IReadOnlyList<FilterTerm> filters,
        ListQueryConfiguration<T> config)
    {
        foreach (var filter in filters)
        {
            if (!config.FilterableFields.TryGetValue(filter.Field, out var field))
            {
                throw new ValidationException("filter", $"Field '{filter.Field}' is not filterable.");
            }

            var parameter = Expression.Parameter(typeof(T), "e");
            var member = Rebind(field.Selector, parameter);
            var predicate = BuildFilterPredicate<T>(member, parameter, field.ValueType, filter);
            source = source.Where(predicate);
        }

        return source;
    }

    private static Expression<Func<T, bool>> BuildFilterPredicate<T>(
        Expression member,
        ParameterExpression parameter,
        Type valueType,
        FilterTerm filter)
    {
        var underlying = Nullable.GetUnderlyingType(valueType) ?? valueType;

        // string operators
        if (filter.Operator is FilterOperator.Contains or FilterOperator.StartsWith)
        {
            if (member.Type != typeof(string))
            {
                throw new ValidationException("filter",
                    $"Operator '{filter.Operator}' is only valid on text fields.");
            }

            var method = filter.Operator == FilterOperator.Contains ? StringContains : StringStartsWith;
            var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
            var call = Expression.Call(member, method, Expression.Constant(filter.Value));
            return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(notNull, call), parameter);
        }

        var typedValue = ParseValue(underlying, filter.Value, filter.Field);
        var constant = Expression.Constant(typedValue, valueType);
        var left = member.Type == valueType ? member : Expression.Convert(member, valueType);

        Expression body = filter.Operator switch
        {
            FilterOperator.Equals => Expression.Equal(left, constant),
            FilterOperator.NotEquals => Expression.NotEqual(left, constant),
            FilterOperator.GreaterThan => Expression.GreaterThan(left, constant),
            FilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(left, constant),
            FilterOperator.LessThan => Expression.LessThan(left, constant),
            FilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(left, constant),
            _ => throw new ValidationException("filter", $"Unsupported operator '{filter.Operator}'."),
        };

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static object? ParseValue(Type targetType, string raw, string field)
    {
        try
        {
            if (targetType == typeof(string))
            {
                return raw;
            }

            if (targetType == typeof(Guid))
            {
                return Guid.Parse(raw);
            }

            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, raw, ignoreCase: true);
            }

            var converter = TypeDescriptor.GetConverter(targetType);
            if (converter.CanConvertFrom(typeof(string)))
            {
                return converter.ConvertFromInvariantString(raw);
            }

            return Convert.ChangeType(raw, targetType, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException
                                      or ArgumentException or NotSupportedException)
        {
            throw new ValidationException(
                "filter", $"Value '{raw}' is not valid for field '{field}'.");
        }
    }

    private static IQueryable<T> ApplySearch<T>(
        IQueryable<T> source,
        string? search,
        ListQueryConfiguration<T> config)
    {
        if (string.IsNullOrWhiteSpace(search) || config.SearchableFields.Count == 0)
        {
            return source;
        }

        var parameter = Expression.Parameter(typeof(T), "e");
        Expression? combined = null;

        foreach (var selector in config.SearchableFields)
        {
            var member = Rebind(selector, parameter);
            var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
            var contains = Expression.Call(member, StringContains, Expression.Constant(search));
            var term = Expression.AndAlso(notNull, contains);
            combined = combined is null ? term : Expression.OrElse(combined, term);
        }

        var predicate = Expression.Lambda<Func<T, bool>>(combined!, parameter);
        return source.Where(predicate);
    }

    private static IQueryable<T> ApplyOrdering<T>(
        IQueryable<T> source,
        IReadOnlyList<SortField> sorts,
        ListQueryConfiguration<T> config)
    {
        IOrderedQueryable<T>? ordered = null;

        foreach (var sort in sorts)
        {
            if (!config.SortableFields.TryGetValue(sort.Field, out var selector))
            {
                throw new ValidationException("sort", $"Field '{sort.Field}' is not sortable.");
            }

            ordered = ApplySort(ordered ?? source, selector, sort.Direction, ordered is not null);
        }

        // Deterministic tie-break so Skip/Take paging is stable even when the client's sort
        // (or the default) is not unique (Req 9.3/65.1 — stable paging).
        var tieBreak = config.DefaultSort;
        ordered = ApplySort(ordered ?? source, tieBreak.Selector, tieBreak.Direction, ordered is not null);

        return ordered;
    }

    private static IOrderedQueryable<T> ApplySort<T>(
        IQueryable<T> source,
        LambdaExpression selector,
        SortDirection direction,
        bool isSubsequent)
    {
        var keyType = selector.ReturnType;
        var descending = direction == SortDirection.Descending;
        var methodName = (isSubsequent, descending) switch
        {
            (false, false) => nameof(Queryable.OrderBy),
            (false, true) => nameof(Queryable.OrderByDescending),
            (true, false) => nameof(Queryable.ThenBy),
            (true, true) => nameof(Queryable.ThenByDescending),
        };

        var call = Expression.Call(
            typeof(Queryable),
            methodName,
            [typeof(T), keyType],
            source.Expression,
            Expression.Quote(selector));

        return (IOrderedQueryable<T>)source.Provider.CreateQuery<T>(call);
    }

    /// <summary>
    /// Rebind a stored single-parameter lambda's body onto a fresh parameter so several
    /// allow-listed selectors can be composed into one predicate over the same range variable.
    /// </summary>
    private static Expression Rebind(LambdaExpression selector, ParameterExpression parameter)
    {
        return new ParameterReplacer(selector.Parameters[0], parameter).Visit(selector.Body);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}

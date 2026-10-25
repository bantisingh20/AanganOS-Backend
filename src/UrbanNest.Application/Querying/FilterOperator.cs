namespace UrbanNest.Application.Querying;

/// <summary>
/// The closed set of comparison operators a client may request in a filter. Deliberately small
/// and allow-listed — never arbitrary dynamic LINQ — so filters stay translatable to SQL and
/// safe from injection (Req 9.5, 65.1). Each entity additionally allow-lists which fields a
/// filter may target via its <see cref="ListQueryConfiguration{T}"/>.
/// </summary>
public enum FilterOperator
{
    Equals = 0,
    NotEquals = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5,
    Contains = 6,
    StartsWith = 7,
}

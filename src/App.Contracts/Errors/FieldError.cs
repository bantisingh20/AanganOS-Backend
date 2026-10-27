namespace App.Contracts.Errors;

/// <summary>
/// One invalid field in a validation error, naming the field and a safe human reason (Req 9.2).
/// </summary>
/// <param name="Field">The offending field name (camelCase, matching the request DTO).</param>
/// <param name="Reason">A safe, human-readable reason the field is invalid.</param>
public sealed record FieldError(string Field, string Reason);

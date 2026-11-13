using App.Contracts.Errors;

namespace App.Application.Exceptions;

/// <summary>
/// Base for semantic application/domain exceptions. Services throw these so the API's global
/// exception middleware can map a known <see cref="Code"/> to the correct envelope and HTTP
/// status (design error table). The <see cref="Exception.Message"/> is a safe, client-facing
/// message — never put secrets, SQL, or stack detail here. Anything that is NOT an
/// <see cref="AppException"/> is treated as an unexpected fault and collapses to a generic
/// INTERNAL_ERROR with no leaked detail (Req 10.2).
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>The stable error code this exception maps to.</summary>
    public ErrorCode Code { get; }

    /// <summary>Per-field validation reasons, when applicable (Req 9.2). Null for non-validation errors.</summary>
    public virtual IReadOnlyList<FieldError>? FieldErrors => null;
}

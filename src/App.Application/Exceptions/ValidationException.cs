using App.Contracts.Errors;

namespace App.Application.Exceptions;

/// <summary>
/// One or more request fields failed validation. Carries per-field reasons surfaced as the
/// envelope's <c>fieldErrors</c> array (Req 9.2). Maps to VALIDATION_ERROR / 400.
/// </summary>
public sealed class ValidationException : AppException
{
    public ValidationException(IReadOnlyList<FieldError> fieldErrors, string? message = null)
        // One field error -> show its reason as the main message (e.g. "A ticket cannot move from
        // 'assigned' to 'new'..."), so the client never sees only the generic text.
        : base(ErrorCode.ValidationError,
            message ?? (fieldErrors is { Count: 1 } ? fieldErrors[0].Reason : "One or more fields are invalid."))
    {
        _fieldErrors = fieldErrors ?? [];
    }

    public ValidationException(string field, string reason, string? message = null)
        : this([new FieldError(field, reason)], message)
    {
    }

    private readonly IReadOnlyList<FieldError> _fieldErrors;

    public override IReadOnlyList<FieldError> FieldErrors => _fieldErrors;
}

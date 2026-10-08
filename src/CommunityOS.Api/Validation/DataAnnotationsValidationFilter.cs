using System.ComponentModel.DataAnnotations;
using CommunityOS.Contracts.Errors;
using AppValidationException = CommunityOS.Application.Exceptions.ValidationException;

namespace CommunityOS.Api.Validation;

/// <summary>
/// Minimal-API endpoint filter that validates request DTO arguments at the trust boundary using
/// their <see cref="ValidationAttribute"/> DataAnnotations. Any failure is collapsed into an
/// application <see cref="AppValidationException"/> carrying one <see cref="FieldError"/> per invalid member
/// (camelCased field names to match the request shape) so the request short-circuits with the
/// VALIDATION_ERROR envelope before any service runs (Req 9.2).
///
/// Attach with <c>.AddEndpointFilter&lt;DataAnnotationsValidationFilter&gt;()</c>. Controllers get
/// the equivalent via the framework's model-state pipeline; this filter covers the minimal-API
/// surface used in the foundation phase.
/// </summary>
public sealed class DataAnnotationsValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var fieldErrors = new List<FieldError>();

        foreach (var argument in context.Arguments)
        {
            if (argument is null)
            {
                continue;
            }

            var type = argument.GetType();

            // Only validate application DTOs — skip framework/primitive args (HttpContext, tokens, ...).
            if (type.IsPrimitive || type == typeof(string) || !IsValidatable(type))
            {
                continue;
            }

            var results = new List<ValidationResult>();
            var validationContext = new ValidationContext(argument);
            if (!Validator.TryValidateObject(argument, validationContext, results, validateAllProperties: true))
            {
                foreach (var result in results)
                {
                    var members = result.MemberNames.Any() ? result.MemberNames : [string.Empty];
                    foreach (var member in members)
                    {
                        fieldErrors.Add(new FieldError(
                            ToCamelCase(member),
                            result.ErrorMessage ?? "Invalid value."));
                    }
                }
            }
        }

        if (fieldErrors.Count > 0)
        {
            throw new AppValidationException(fieldErrors);
        }

        return await next(context);
    }

    // A type carrying any DataAnnotations metadata is treated as a validatable DTO.
    private static bool IsValidatable(Type type)
        => type.IsClass || (type.IsValueType && !type.IsEnum);

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

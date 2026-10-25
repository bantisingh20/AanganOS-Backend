namespace UrbanNest.Api.Middleware;

/// <summary>Pipeline wiring for <see cref="ExceptionHandlingMiddleware"/>.</summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    /// <summary>
    /// Registers the terminal exception-handling middleware. Place this first (outermost) so it
    /// wraps the whole pipeline and can render the error envelope for anything thrown downstream.
    /// </summary>
    public static IApplicationBuilder UseExceptionEnvelope(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();

    /// <summary>
    /// Registers the correlation-id stage. Place just inside the exception envelope so the id is
    /// established before anything else and the envelope can reuse it.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();

    /// <summary>
    /// Registers the API-level permission authorization stage. Place after authentication and
    /// tenant-context resolution and before endpoint execution.
    /// </summary>
    public static IApplicationBuilder UsePermissionAuthorization(this IApplicationBuilder app)
        => app.UseMiddleware<PermissionAuthorizationMiddleware>();
}

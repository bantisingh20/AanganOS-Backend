namespace App.Api.Middleware;

/// <summary>Pipeline wiring for <see cref="ExceptionHandlingMiddleware"/>.</summary>
public static class ExceptionHandlingMiddlewareExtensions
{

    public static IApplicationBuilder UseExceptionEnvelope(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();


    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();


    public static IApplicationBuilder UsePermissionAuthorization(this IApplicationBuilder app)
        => app.UseMiddleware<PermissionAuthorizationMiddleware>();
}

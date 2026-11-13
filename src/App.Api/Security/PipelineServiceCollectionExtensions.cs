using System.IdentityModel.Tokens.Jwt;
using System.Text;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Auth;
using App.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace App.Api.Security;

/// <summary>
/// Registers the request-pipeline security services: JWT bearer authentication (config-driven),
/// the principal-derived request-scoped <see cref="ITenantContext"/>, and the permission-evaluator
/// seam. Composition is grouped here so Program.cs reads as the pipeline order, not wiring detail.
/// </summary>
public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddRequestPipelineSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // JWT options from the "Jwt" section (options pattern, validated on start). Secret is never
        // in source — it comes from configuration/user-secrets/environment.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Configure the bearer scheme from the resolved JwtOptions. Done via the options pattern
        // (not an eager configuration.Get at registration) so the signing key/issuer/audience are
        // read after all configuration sources are composed — hosts and tests can override them.
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerFromOptions>();

        services.AddAuthorization();

        // The authenticated principal -> ITenantContext bridge needs HttpContext.
        services.AddHttpContextAccessor();

        // Replace the Infrastructure fail-closed NullTenantContext with the principal-derived one
        // for this host. Remove the TryAdd'd default first so our scoped registration wins.
        services.RemoveAll<ITenantContext>();
        services.AddScoped<ITenantContext, HttpContextTenantContext>();

        // Permission-evaluator fallback. The REAL Authorization_Service (Task 5.3) is registered by
        // AddInfrastructure, which runs before this method, so it already occupies
        // IPermissionEvaluator and this TryAdd no-ops. The permissive placeholder remains only as a
        // fail-safe for a host that wires pipeline security without the infrastructure layer.
        services.TryAddScoped<IPermissionEvaluator, AllowAuthenticatedPermissionEvaluator>();

        return services;
    }

    /// <summary>
    /// Applies <see cref="JwtOptions"/> (resolved from the composed configuration) to the JWT
    /// bearer scheme. Reads <c>sub</c> as the name claim and keeps raw claim names so the
    /// App claim contract survives into the principal.
    /// </summary>
    private sealed class ConfigureJwtBearerFromOptions(IOptions<JwtOptions> jwtOptions)
        : IConfigureNamedOptions<JwtBearerOptions>
    {
        private readonly JwtOptions _jwt = jwtOptions.Value;

        public void Configure(string? name, JwtBearerOptions options)
        {
            if (name != JwtBearerDefaults.AuthenticationScheme)
            {
                return;
            }

            Configure(options);
        }

        public void Configure(JwtBearerOptions options)
        {
            options.MapInboundClaims = false; // keep raw claim names (sub, communityos:*) intact
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = string.IsNullOrEmpty(_jwt.SigningKey)
                    ? null
                    : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(_jwt.ClockSkewSeconds),
                NameClaimType = CommunityClaimTypes.UserId,
            };

            // Session-revocation hook (Req 1.3): a signature-valid token is additionally rejected if
            // its session (jti) has been revoked by logout. The store is resolved from the request
            // scope so it uses the per-request DbContext; the lookup is a single indexed read.
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var tokenId = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                    if (string.IsNullOrEmpty(tokenId))
                    {
                        context.Fail("Token is missing a session identifier.");
                        return;
                    }

                    var store = context.HttpContext.RequestServices
                        .GetRequiredService<ISessionRevocationStore>();
                    if (!await store.IsSessionActiveAsync(tokenId, context.HttpContext.RequestAborted))
                    {
                        context.Fail("Session has been revoked.");
                    }
                },
            };
        }
    }
}

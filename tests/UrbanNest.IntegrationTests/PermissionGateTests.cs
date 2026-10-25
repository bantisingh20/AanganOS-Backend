using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UrbanNest.Application.Abstractions;
using UrbanNest.Application.Abstractions.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace UrbanNest.IntegrationTests;

/// <summary>
/// Proves the FORBIDDEN plumbing of the authorization seam: when the pluggable
/// <see cref="IPermissionEvaluator"/> denies (as the real Task 5.3 evaluator will for a user
/// lacking the permission), a protected endpoint short-circuits with 403 FORBIDDEN and the
/// operation never runs (Req 4.2). An authenticated-but-permitted call still succeeds.
/// </summary>
public sealed class PermissionGateTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class DenyAllPermissionEvaluator : IPermissionEvaluator
    {
        public ValueTask<bool> IsPermittedAsync(
            ITenantContext tenant, PermissionRequirement requirement, CancellationToken ct = default)
            => ValueTask.FromResult(false);
    }

    [Fact]
    public async Task Denied_permission_returns_403_forbidden()
    {
        using var factory = new PipelineWebApplicationFactory();

        // Plug in a denying evaluator exactly where Task 5.3's real one will go.
        using var client = factory
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
                s.AddScoped<IPermissionEvaluator, DenyAllPermissionEvaluator>()))
            .CreateClient();

        var token = factory.CreateToken("user-A", communityIds: [Guid.NewGuid()]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/pipeline-demo/my-communities");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("FORBIDDEN", body.GetProperty("error").GetProperty("code").GetString());
    }
}

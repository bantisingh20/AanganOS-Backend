using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UrbanNest.Api.Middleware;

namespace UrbanNest.IntegrationTests;

/// <summary>
/// End-to-end runnable check for the Task 2.2 pipeline (correlation → auth → tenant context →
/// authorization → validation), exercised through the real host:
/// <list type="bullet">
///   <item><description>an authenticated request builds a tenant context whose AuthorizedCommunityIds come from the token claims and flows into AppDbContext (the user sees only their communities' rows);</description></item>
///   <item><description>an unauthenticated request to a protected endpoint returns 401 UNAUTHENTICATED;</description></item>
///   <item><description>an invalid DTO returns 400 VALIDATION_ERROR with per-field fieldErrors;</description></item>
///   <item><description>the correlation id is present on the response and reused from the incoming header.</description></item>
/// </list>
/// </summary>
public sealed class RequestPipelineTests : IClassFixture<PipelineWebApplicationFactory>
{
    private readonly PipelineWebApplicationFactory _factory;

    public RequestPipelineTests(PipelineWebApplicationFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Authenticated_request_sees_only_its_own_communities_rows_end_to_end()
    {
        var communityA = Guid.NewGuid();
        var communityB = Guid.NewGuid();
        await _factory.SeedAsync(
            (communityA, "A-1"),
            (communityA, "A-2"),
            (communityB, "B-1"));

        var client = _factory.CreateClient();
        // Token authorizes community A only — never a client-supplied CommunityId.
        var token = _factory.CreateToken("user-A", communityIds: [communityA]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/pipeline-demo/my-communities");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var data = body.GetProperty("data");

        Assert.Equal("user-A", data.GetProperty("userId").GetString());
        var authorized = data.GetProperty("authorizedCommunityIds").EnumerateArray()
            .Select(e => Guid.Parse(e.GetString()!)).ToArray();
        Assert.Equal([communityA], authorized);
        Assert.False(data.GetProperty("isPlatformSuperAdmin").GetBoolean());

        // The global tenant filter, driven by the claim-derived context, returns only A's 2 rows.
        Assert.Equal(2, data.GetProperty("visibleRowCount").GetInt32());
    }

    [Fact]
    public async Task Super_admin_token_sees_rows_across_communities()
    {
        var communityA = Guid.NewGuid();
        var communityB = Guid.NewGuid();
        var factory = new PipelineWebApplicationFactory();
        await factory.SeedAsync((communityA, "A-1"), (communityB, "B-1"));

        var client = factory.CreateClient();
        var token = factory.CreateToken("admin", isSuperAdmin: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/pipeline-demo/my-communities");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var data = body.GetProperty("data");
        Assert.True(data.GetProperty("isPlatformSuperAdmin").GetBoolean());
        Assert.Equal(2, data.GetProperty("visibleRowCount").GetInt32());

        factory.Dispose();
    }

    [Fact]
    public async Task Unauthenticated_request_to_protected_endpoint_returns_401_unauthenticated()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/pipeline-demo/my-communities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("UNAUTHENTICATED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Invalid_dto_returns_400_validation_error_with_field_errors()
    {
        var client = _factory.CreateClient();
        var token = _factory.CreateToken("user-A", communityIds: [Guid.NewGuid()]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // name blank, age out of range → two field errors.
        var response = await client.PostAsJsonAsync("/pipeline-demo/echo", new { name = "", age = 999 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());

        var fields = body.GetProperty("error").GetProperty("fieldErrors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()).ToArray();
        Assert.Contains("name", fields);
        Assert.Contains("age", fields);
    }

    [Fact]
    public async Task Correlation_id_is_present_and_reused_from_incoming_header()
    {
        var client = _factory.CreateClient();
        var incoming = "test-correlation-" + Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdMiddleware.CorrelationIdHeader, incoming);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.CorrelationIdHeader, out var values));
        Assert.Equal(incoming, values!.Single());
    }

    [Fact]
    public async Task Correlation_id_is_generated_when_no_incoming_header()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.CorrelationIdHeader, out var values));
        Assert.False(string.IsNullOrWhiteSpace(values!.Single()));
    }
}

using System.Text.Json;
using CommunityOS.Api.Middleware;
using CommunityOS.Application.Exceptions;
using CommunityOS.Contracts.Envelope;
using CommunityOS.Contracts.Errors;
using CommunityOS.Contracts.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommunityOS.UnitTests;

/// <summary>
/// Runnable check for Task 2.1: the response envelope shape, validation field-errors, the
/// unknown-exception → generic INTERNAL_ERROR (no leaked detail) rule (Req 10.2), and the
/// code → HTTP status mapping. Property-based Property-14 / page-size tests are tasks 2.4/2.5.
/// </summary>
public sealed class ResponseEnvelopeTests
{
    private static readonly JsonSerializerOptions Json = EnvelopeJson.CreateOptions();

    // Drives a request through the middleware and returns (statusCode, parsed JSON root).
    private static async Task<(int Status, JsonElement Body)> RunAsync(RequestDelegate next, HttpContext? ctx = null)
    {
        var context = ctx ?? new DefaultHttpContext();
        var buffer = new MemoryStream();
        context.Response.Body = buffer;

        var middleware = new ExceptionHandlingMiddleware(
            next,
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            Json);

        await middleware.InvokeAsync(context);

        buffer.Position = 0;
        using var doc = await JsonDocument.ParseAsync(buffer);
        return (context.Response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task Success_envelope_has_camelCase_shape_with_data_and_null_error()
    {
        // A handler that writes a success envelope itself (what controllers will do later).
        var result = ApiResult<object>.Ok(new { name = "Tower A" }, "corr-123");

        RequestDelegate next = async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(result, Json);
        };

        var (status, body) = await RunAsync(next);

        Assert.Equal(200, status);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("corr-123", body.GetProperty("correlationId").GetString());
        Assert.Equal("Tower A", body.GetProperty("data").GetProperty("name").GetString());
        // Error omitted when null (camelCase + WhenWritingNull).
        Assert.False(body.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Validation_exception_produces_fieldErrors_envelope()
    {
        RequestDelegate next = _ => throw new ValidationException(
            [new FieldError("unitNumber", "already exists in community")]);

        var (status, body) = await RunAsync(next);

        Assert.Equal(400, status);
        Assert.False(body.GetProperty("success").GetBoolean());
        var error = body.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        var fieldErrors = error.GetProperty("fieldErrors");
        Assert.Equal(1, fieldErrors.GetArrayLength());
        Assert.Equal("unitNumber", fieldErrors[0].GetProperty("field").GetString());
        Assert.Equal("already exists in community", fieldErrors[0].GetProperty("reason").GetString());
        // Correlation id present even on failure (Req 10.1).
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task Unknown_exception_maps_to_generic_internal_error_without_leaking_detail()
    {
        const string secret = "connection string pwd=SuperSecret; stack at Foo.Bar()";
        RequestDelegate next = _ => throw new InvalidOperationException(secret);

        var (status, body) = await RunAsync(next);

        Assert.Equal(500, status);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("INTERNAL_ERROR", body.GetProperty("error").GetProperty("code").GetString());

        // The whole serialized body must not leak the internal message/stack (Req 10.2).
        var raw = body.GetRawText();
        Assert.DoesNotContain("SuperSecret", raw);
        Assert.DoesNotContain("Foo.Bar", raw);
        Assert.DoesNotContain(nameof(InvalidOperationException), raw);
    }

    [Fact]
    public async Task Correlation_id_from_incoming_header_is_reused()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ExceptionHandlingMiddleware.CorrelationIdHeader] = "upstream-id";

        RequestDelegate next = _ => throw new NotFoundException();

        var (status, body) = await RunAsync(next, context);

        Assert.Equal(404, status);
        Assert.Equal("upstream-id", body.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData(ErrorCode.ValidationError, "VALIDATION_ERROR", 400)]
    [InlineData(ErrorCode.Unauthenticated, "UNAUTHENTICATED", 401)]
    [InlineData(ErrorCode.Forbidden, "FORBIDDEN", 403)]
    [InlineData(ErrorCode.NotFound, "NOT_FOUND", 404)]
    [InlineData(ErrorCode.Conflict, "CONFLICT", 409)]
    [InlineData(ErrorCode.IdempotentReplay, "IDEMPOTENT_REPLAY", 200)]
    [InlineData(ErrorCode.RateLimited, "RATE_LIMITED", 429)]
    [InlineData(ErrorCode.PayloadTooLarge, "PAYLOAD_TOO_LARGE", 413)]
    [InlineData(ErrorCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE", 415)]
    [InlineData(ErrorCode.IntegrationFailure, "INTEGRATION_FAILURE", 502)]
    [InlineData(ErrorCode.InternalError, "INTERNAL_ERROR", 500)]
    public void Error_code_maps_to_stable_wire_string_and_http_status(ErrorCode code, string wire, int httpStatus)
    {
        Assert.Equal(wire, ErrorCodes.ToWire(code));
        Assert.Equal(httpStatus, ErrorCodes.ToHttpStatus(code));
    }

    [Fact]
    public async Task Known_app_exceptions_map_to_their_codes_and_statuses()
    {
        await AssertMaps(new UnauthenticatedException(), 401, "UNAUTHENTICATED");
        await AssertMaps(new ForbiddenException(), 403, "FORBIDDEN");
        await AssertMaps(new ConflictException(), 409, "CONFLICT");
        await AssertMaps(new IntegrationFailureException(), 502, "INTEGRATION_FAILURE");
    }

    private static async Task AssertMaps(AppException ex, int expectedStatus, string expectedCode)
    {
        RequestDelegate next = _ => throw ex;
        var (status, body) = await RunAsync(next);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedCode, body.GetProperty("error").GetProperty("code").GetString());
    }
}

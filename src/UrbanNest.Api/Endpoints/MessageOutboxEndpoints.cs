using UrbanNest.Api.Security;
using UrbanNest.Application.Messaging;
using UrbanNest.Application.Querying;
using UrbanNest.Contracts.Envelope;
using UrbanNest.Domain.Identity;

namespace UrbanNest.Api.Endpoints;

public static class MessageOutboxEndpoints
{
    public static IEndpointRouteBuilder MapMessageOutboxEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/message-outbox", async (
                string? channel,
                string? status,
                string? recipient,
                string? fromDate,
                string? toDate,
                string? search,
                string? sort,
                int? page,
                int? pageSize,
                IMessageOutboxQueryService messages,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                if (!string.IsNullOrWhiteSpace(channel))
                {
                    filters.Add(new FilterTerm("channel", FilterOperator.Equals, channel));
                }
                if (!string.IsNullOrWhiteSpace(status))
                {
                    filters.Add(new FilterTerm("status", FilterOperator.Equals, status));
                }
                if (!string.IsNullOrWhiteSpace(recipient))
                {
                    filters.Add(new FilterTerm("recipient", FilterOperator.Contains, recipient));
                }
                if (!string.IsNullOrWhiteSpace(fromDate))
                {
                    filters.Add(new FilterTerm("createdAt", FilterOperator.GreaterThanOrEqual, fromDate));
                }
                if (!string.IsNullOrWhiteSpace(toDate))
                {
                    filters.Add(new FilterTerm("createdAt", FilterOperator.LessThanOrEqual, toDate));
                }

                var parameters = new ListQueryParameters
                {
                    Page = page,
                    PageSize = pageSize,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search,
                    Filters = filters,
                    Sort = EndpointSort.Parse(sort),
                };

                var data = await messages.QueryAsync(parameters, ct);
                return Results.Ok(ApiResult<PagedData<MessageOutboxLogDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Audit"));

        return app;
    }
}

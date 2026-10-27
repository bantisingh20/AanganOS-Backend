using App.Api.Security;
using App.Application.Notifications;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;

public static class NotificationOutboxAdminEndpoints
{
    public static IEndpointRouteBuilder MapNotificationOutboxAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/notification-outbox", async (
                string? status,
                string? category,
                string? templateKey,
                string? recipientUserId,
                string? fromDate,
                string? toDate,
                string? search,
                string? sort,
                int? page,
                int? pageSize,
                INotificationOutboxQueryService notifications,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                if (!string.IsNullOrWhiteSpace(status))
                {
                    filters.Add(new FilterTerm("status", FilterOperator.Equals, status));
                }
                if (!string.IsNullOrWhiteSpace(category))
                {
                    filters.Add(new FilterTerm("category", FilterOperator.Equals, category));
                }
                if (!string.IsNullOrWhiteSpace(templateKey))
                {
                    filters.Add(new FilterTerm("templateKey", FilterOperator.Equals, templateKey));
                }
                if (!string.IsNullOrWhiteSpace(recipientUserId))
                {
                    filters.Add(new FilterTerm("recipientUserId", FilterOperator.Equals, recipientUserId));
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

                var data = await notifications.QueryAsync(parameters, ct);
                return Results.Ok(ApiResult<PagedData<NotificationOutboxLogDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Audit"));

        return app;
    }
}

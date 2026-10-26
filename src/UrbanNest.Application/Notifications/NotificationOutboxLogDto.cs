namespace UrbanNest.Application.Notifications;

public sealed record NotificationOutboxLogDto(
    Guid Id,
    Guid CommunityId,
    string TemplateKey,
    string Category,
    string Channels,
    Guid RecipientUserId,
    string? TargetPlayerId,
    string Title,
    string Body,
    string Status,
    int Attempts,
    DateTime? NextAttemptUtc,
    string? LastError,
    bool IsRead,
    DateTime CreatedAtUtc);

public interface INotificationOutboxQueryService
{
    Task<Contracts.Envelope.PagedData<NotificationOutboxLogDto>> QueryAsync(
        Querying.ListQueryParameters parameters,
        CancellationToken cancellationToken = default);
}

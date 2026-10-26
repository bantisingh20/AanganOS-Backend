namespace UrbanNest.Application.Messaging;

public sealed record MessageOutboxLogDto(
    Guid Id,
    string Channel,
    string Recipient,
    string? Subject,
    string Body,
    string Status,
    int Attempts,
    DateTime? NextAttemptUtc,
    string? LastError,
    DateTime CreatedAtUtc);

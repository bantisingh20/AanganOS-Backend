namespace UrbanNest.Domain.Notifications;

/// <summary>
/// A delivery channel a notification template may target (Req 11.1, 64). Phase 1 dispatches
/// <see cref="Push"/> through OneSignal (Task 7.2); the other channels are modeled now so the
/// template/outbox schema already carries the full matrix (Req 64) and later phases light up SMS/
/// email/WhatsApp dispatchers without a schema change. Stored as a bit flag so one template can
/// declare several channels (e.g. push + SMS for a visitor request, Req 64.1).
/// </summary>
[Flags]
public enum NotificationChannel
{
    None = 0,
    Push = 1 << 0,
    Sms = 1 << 1,
    Email = 1 << 2,
    WhatsApp = 1 << 3,
}

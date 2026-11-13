using App.Application.Gate;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Gate;

/// <summary>
/// No-op/logging default <see cref="IAccessControlIntegration"/> until the real hardware drivers
/// arrive in Phase 12 (Task 40). It records that a hardware action WOULD have been triggered — never
/// contacting any device — and reports a non-fatal "no device configured" outcome so the
/// Gate_Service still writes a complete <c>AccessIntegrationEvent</c> audit row (Req 27.5) and the
/// gate operation is never blocked. Swapping in the real CCTV / boom-barrier / access-control driver
/// is a one-line DI change; the Gate_Service does not change (the same seam pattern as the logging
/// OTP deliverer / OneSignal client).
/// </summary>
public sealed class LoggingAccessControlIntegration(ILogger<LoggingAccessControlIntegration> logger)
    : IAccessControlIntegration
{
    public Task<AccessIntegrationResult> TriggerAsync(AccessIntegrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        logger.LogInformation(
            "Access-control integration '{Action}' requested for community {CommunityId} gate {GateLabel} — no hardware driver configured (Phase 12 / Task 40); recording a no-op integration event.",
            command.Action,
            command.CommunityId,
            command.GateLabel ?? command.GateId?.ToString() ?? "<unspecified>");

        // Non-fatal: no device attached in this phase, so report a no-op outcome. The Gate_Service
        // records this on the event row (status 'triggered') rather than throwing.
        return Task.FromResult(AccessIntegrationResult.Failed("No access-control hardware configured (Phase 12 / Task 40)."));
    }
}

namespace CommunityOS.Application.Gate;

/// <summary>
/// Access-control hardware seam (Req 27.5): the Gate_Service calls this to open/close a boom
/// barrier, request a CCTV snapshot or push a generic access-control event. It is deliberately a
/// THIN, provider-agnostic seam — exactly like <c>IOneSignalClient</c> for push — so the real
/// device drivers can be plugged in later without touching the Gate_Service.
///
/// <para><b>Phase 12 boundary (Task 40).</b> This phase ships only the seam and a no-op/logging
/// default implementation (<c>LoggingAccessControlIntegration</c>): the Gate_Service records every
/// trigger as an <c>AccessIntegrationEvent</c> row regardless of whether a real device is attached,
/// so the audit trail is complete out of the box. The actual CCTV / boom-barrier / access-control
/// hardware drivers (vendor protocols, device registry, retry/timeout policy, health checks) are
/// the full integrations module in Phase 12 (Task 40); swapping the real driver in is a one-line DI
/// change and the Gate_Service does not change.</para>
///
/// <para><b>Fail-safe (steering: fail safe, not fatal).</b> A hardware trigger must never crash or
/// roll back the originating gate operation. The seam returns an outcome (succeeded / failed) rather
/// than throwing for an unreachable device; the Gate_Service records that outcome on the event row.</para>
/// </summary>
public interface IAccessControlIntegration
{
    /// <summary>
    /// Trigger a hardware/access-control action for a gate (Req 27.5). Returns the outcome the
    /// Gate_Service records on the <c>AccessIntegrationEvent</c>. Implementations must not throw for
    /// an unreachable/absent device — they return <see cref="AccessIntegrationResult.Failed"/> so the
    /// gate operation is never blocked.
    /// </summary>
    Task<AccessIntegrationResult> TriggerAsync(AccessIntegrationCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// What the Gate_Service asks the hardware seam to do (Req 27.5).
/// </summary>
/// <param name="CommunityId">Owning community (tenant scoping of the recorded event).</param>
/// <param name="Action">The action code from the <c>Access_Integration_Action</c> list (open_barrier, cctv_snapshot, ...).</param>
/// <param name="GateId">Optional target gate id (the Gate master is a later task).</param>
/// <param name="GateLabel">Optional free-text gate/device label.</param>
/// <param name="Detail">Optional small, secret-free detail (reason/reference).</param>
public sealed record AccessIntegrationCommand(
    Guid CommunityId,
    string Action,
    Guid? GateId,
    string? GateLabel,
    string? Detail);

/// <summary>
/// Outcome of a hardware trigger the Gate_Service records on the event row (Req 27.5). Carries only
/// a success flag and an optional secret-free note — never device internals.
/// </summary>
/// <param name="Succeeded">Whether the seam reported the action succeeded (false for a no-op/logging default or an unreachable device).</param>
/// <param name="Note">Optional small, secret-free note (e.g. "no device configured").</param>
public sealed record AccessIntegrationResult(bool Succeeded, string? Note)
{
    /// <summary>A failed/no-op outcome with an optional note.</summary>
    public static AccessIntegrationResult Failed(string? note = null) => new(false, note);

    /// <summary>A successful outcome with an optional note.</summary>
    public static AccessIntegrationResult Ok(string? note = null) => new(true, note);
}

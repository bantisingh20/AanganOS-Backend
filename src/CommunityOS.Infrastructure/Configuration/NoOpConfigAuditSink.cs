using CommunityOS.Application.Abstractions.Audit;

namespace CommunityOS.Infrastructure.Config;

/// <summary>
/// Placeholder <see cref="IConfigAuditSink"/> until Task 4.1 (Audit_Service) exists. Does nothing
/// and never throws, so a config edit is never blocked by the missing audit backend. Task 4.1
/// replaces this registration with the real append-only Audit_Log sink; Config_Service is unchanged.
///
/// ponytail: intentionally empty — the seam, not the implementation, is Task 3.1's responsibility.
/// </summary>
public sealed class NoOpConfigAuditSink : IConfigAuditSink
{
    public Task RecordConfigChangeAsync(ConfigChange change, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

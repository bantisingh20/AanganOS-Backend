using System.Text.Json;
using CommunityOS.Application.Abstractions.Audit;
using CommunityOS.Application.Audit;
using Microsoft.Extensions.Logging;

namespace CommunityOS.Infrastructure.Config;

/// <summary>
/// The REAL <see cref="IConfigAuditSink"/> (Task 4.1) that writes Config_Service edits to the
/// append-only audit trail via <see cref="IAuditService"/> — replacing <c>NoOpConfigAuditSink</c>.
/// So a config/master-data edit now produces a genuine <c>AuditLog</c> entry end to end (Req 8.2).
///
/// <para><b>Fail-safe, non-blocking (seam contract).</b> Config auditing is a non-critical
/// side-effect: the configuration change is already persisted before this runs. Per the seam's
/// contract (and the project's reliability guidance — a non-critical audit failure must not break
/// the business op) a failure to append is logged and swallowed, never propagated into the config
/// edit. Critical financial/custody audit uses its own in-transaction path later; config edits are
/// not in that class.</para>
///
/// <para><b>Secret redaction (steering: never log secret values).</b> The before/after values are
/// summarised into the audit detail only after redacting any key that names a secret
/// (password/otp/token/secret/key), so a config value that holds a credential is recorded as
/// <c>***</c> rather than in the clear.</para>
/// </summary>
public sealed class AuditConfigAuditSink(IAuditService audit, ILogger<AuditConfigAuditSink> logger)
    : IConfigAuditSink
{
    public async Task RecordConfigChangeAsync(ConfigChange change, CancellationToken cancellationToken = default)
    {
        try
        {
            var secret = LooksSecret(change.Key);
            var detail = JsonSerializer.Serialize(new
            {
                change.Key,
                oldValue = Redact(change.OldValue, secret),
                newValue = Redact(change.NewValue, secret),
            });

            await audit.AppendAsync(
                new AuditEntry(
                    Action: change.OldValue is null ? "Create" : "Update",
                    ResourceType: change.ResourceType,
                    ResourceId: change.Key,
                    CommunityId: change.CommunityId,
                    Detail: detail),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Non-critical: the config edit already committed. Log and continue so a transient
            // audit-write failure never fails the user's config change (seam contract / reliability).
            logger.LogError(ex,
                "Failed to append config audit entry for {ResourceType} key {Key}; config change already persisted.",
                change.ResourceType, change.Key);
        }
    }

    // A value under a key that names a credential must never be recorded in the clear.
    private static bool LooksSecret(string key)
    {
        ReadOnlySpan<string> markers = ["password", "pwd", "secret", "otp", "token", "apikey", "api_key", "privatekey", "private_key"];
        foreach (var marker in markers)
        {
            if (key.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? Redact(string? value, bool secret)
        => value is null ? null : secret ? "***" : value;
}

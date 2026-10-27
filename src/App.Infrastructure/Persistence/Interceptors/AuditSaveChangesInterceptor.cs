using App.Application.Abstractions;
using App.Domain.Audit;
using App.Domain.Common;
using App.Domain.Maintenance;
using App.Domain.Parcel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace App.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps audit columns on insert/update and converts hard deletes of soft-deletable
/// entities into soft deletes (sets <see cref="ISoftDeletable.IsDeleted"/> +
/// <see cref="ISoftDeletable.DeletedAtUtc"/>). The acting user and timestamp come from the
/// injected <see cref="ITenantContext"/> / <see cref="IClock"/> — never from the caller's DTO.
///
/// <para><b>Append-only audit enforcement (Req 6.3).</b> This interceptor is also the data-layer
/// guard that makes the <c>AuditLog</c> table append-only: it rejects any <c>AuditLog</c> entry in
/// a <see cref="EntityState.Modified"/> or <see cref="EntityState.Deleted"/> state by throwing. The
/// only valid state for an audit row is <see cref="EntityState.Added"/>. Combined with the fact
/// that <see cref="IAuditService"/> exposes no update/delete method, there is no application path
/// that can edit or remove a persisted audit record — attempting it fails loudly rather than
/// silently corrupting the trail.</para>
/// </summary>
public sealed class AuditSaveChangesInterceptor(ITenantContext tenant, IClock clock)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var actingUser = tenant.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Append-only guard (Req 6.3): an audit row may only ever be inserted. Any attempt to
            // modify or delete one — through tracking a loaded entity and changing it, or Remove —
            // is a tamper/bug and must fail loudly, not be silently dropped.
            if (entry.Entity is AuditLog &&
                entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "AuditLog is append-only: audit records cannot be modified or deleted through the application.");
            }

            // Same append-only guard for the parcel custody timeline (Req 31.1): a custody-timeline
            // entry is recorded once per parcel event and is non-editable through the application —
            // modifying or deleting one is a tamper/bug and must fail loudly. The service only ever
            // inserts, so this never fires on a correct path; it defends the invariant at the data
            // layer, exactly as the AuditLog guard above does.
            if (entry.Entity is CustodyTimelineEntry &&
                entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "CustodyTimelineEntry is append-only: custody-timeline entries cannot be modified or deleted through the application.");
            }

            // Same append-only guard for the asset equipment history (Req 36.5): an equipment-history
            // entry is appended once when a work order is completed and is non-editable through the
            // application — modifying or deleting one is a tamper/bug and must fail loudly. The service
            // only ever inserts, so this never fires on a correct path; it defends the invariant at the
            // data layer, exactly as the AuditLog / CustodyTimelineEntry guards above do.
            if (entry.Entity is EquipmentHistoryEntry &&
                entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "EquipmentHistoryEntry is append-only: equipment-history entries cannot be modified or deleted through the application.");
            }

            // Translate a delete of a soft-deletable entity into a soft delete.
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable deletable)
            {
                entry.State = EntityState.Modified;
                deletable.IsDeleted = true;
                deletable.DeletedAtUtc = now;
            }

            if (entry.Entity is IAuditable auditable)
            {
                StampAudit(entry, auditable, now, actingUser);
            }
        }
    }

    private static void StampAudit(EntityEntry entry, IAuditable auditable, DateTime now, string? actingUser)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                auditable.CreatedAtUtc = now;
                auditable.CreatedBy = actingUser;
                break;
            case EntityState.Modified:
                auditable.UpdatedAtUtc = now;
                auditable.UpdatedBy = actingUser;
                // Never let an update rewrite the original creation stamps.
                entry.Property(nameof(IAuditable.CreatedAtUtc)).IsModified = false;
                entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                break;
        }
    }
}

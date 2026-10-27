namespace App.Domain.Common;

/// <summary>
/// Marks a tenant-owned entity that belongs to a specific resident Unit. The DbContext applies
/// resident self-scoping (Req 5.4) to these entities: a resident-self-scoped request only sees
/// rows whose <see cref="UnitId"/> matches the resident's own unit. Null <see cref="UnitId"/>
/// means the row is not pinned to a single unit (and is therefore hidden from a self-scoped
/// resident).
/// </summary>
public interface IUnitScoped
{
    Guid? UnitId { get; set; }
}

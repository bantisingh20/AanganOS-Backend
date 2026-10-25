using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Files;

/// <summary>
/// Metadata row for one stored file (Req 7.1). This is the opaque reference an owning resource
/// (a Ticket attachment, Parcel evidence photo, Document, ...) persists — the owning resource
/// stores only the <see cref="StoredFile.Id"/>, never a path or public URL (Req 7.4).
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>, so the DbContext's global query filter
/// enforces tenant isolation and soft-delete on every ordinary query: a direct id lookup through
/// EF physically cannot return a foreign-tenant or deleted row (Req 5.2, 5.3, 7.2). File bytes
/// themselves live in the blob store under <see cref="StorageKey"/>, outside any web-served
/// directory; this row is only the metadata + the opaque key that locates them.</para>
/// </summary>
public class StoredFile : TenantOwnedEntity
{
    /// <summary>
    /// The resource type that owns this file (e.g. <c>Ticket</c>, <c>Parcel</c>, <c>Document</c>).
    /// The <see cref="PermissionAction.View"/> check on download is evaluated against this type
    /// (Req 7.2), so an upload binds the file to the resource whose permissions govern it.
    /// </summary>
    public string OwningResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the specific owning resource instance (e.g. the Ticket id). Nullable because a
    /// file may be uploaded before its owning record exists (the caller wires the id afterwards).
    /// </summary>
    public Guid? OwningResourceId { get; set; }

    /// <summary>Original client-supplied file name, kept for display/download only — never used as a path (Req 7.4, path-traversal safety).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The validated MIME content type (checked against the configured allow-list on upload, Req 7.3).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Size of the stored bytes in bytes (validated against the configured maximum on upload, Req 7.3).</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Opaque key that locates the bytes in the blob store (a generated path segment, never derived
    /// from the client file name). The blob store resolves it to a non-public physical location; it
    /// is never exposed to clients and is not a browsable/public URL (Req 7.4).
    /// </summary>
    public string StorageKey { get; set; } = string.Empty;
}

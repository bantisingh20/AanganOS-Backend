namespace CommunityOS.Application.Community;

/// <summary>
/// Bulk import/export of Units (Req 21.1–21.5). Kept as a focused seam separate from the per-record
/// <see cref="IUnitService"/> CRUD so the CSV parsing/formatting, the row cap and the partial-success
/// semantics live together without bloating the single-unit service.
///
/// <para><b>Import (Req 21.1, 21.2).</b> Each CSV row is validated independently (unit number
/// present, type/status valid against the configurable <c>Unit_Type</c>/<c>Unit_Status</c> lists,
/// no duplicate number within the file or against an existing unit); a valid row creates a Unit in
/// the target community, an invalid row is <i>skipped</i> with a per-row reason and processing
/// continues — one bad row never aborts the batch.</para>
///
/// <para><b>Export (Req 21.3, 66.6).</b> Produces CSV of only the Units the caller is permitted to
/// View: the Export⊆View guard plus the EF global tenant filter keep the exported set a subset of
/// the caller's viewable, authorized scope.</para>
///
/// <para><b>Bounded (Req 21.4, 65.2).</b> Both operations are capped at a configured maximum number
/// of rows; an oversized import is rejected (PAYLOAD_TOO_LARGE) before any row is processed, and an
/// export never materializes more than the cap. Both operations are recorded in the Audit_Log
/// (Req 21.5).</para>
/// </summary>
public interface IUnitBulkService
{
    /// <summary>
    /// Import units from CSV text into <paramref name="communityId"/> (Req 21.1, 21.2, 21.4, 21.5).
    /// Validates and creates one Unit per valid row; skips invalid rows with a reason; rejects a
    /// file exceeding the configured row cap with PAYLOAD_TOO_LARGE before processing. Audited.
    /// </summary>
    Task<BulkImportResult> ImportUnitsAsync(
        Guid communityId, string csvContent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Export the units the caller may View as CSV (Req 21.3, 21.4, 21.5, 66.6). Enforces Export⊆View
    /// and the tenant filter, caps the row count to the configured maximum, and audits the export.
    /// Returns the CSV bytes plus a suggested file name for a streamed download.
    /// </summary>
    Task<BulkExportResult> ExportUnitsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a bulk import (Req 21.2): how many rows were created and the per-row rejections
/// (1-based row number as it appeared in the file, excluding the header, plus the reason).
/// </summary>
/// <param name="TotalRows">Data rows read from the file (excludes the header).</param>
/// <param name="CreatedCount">Number of rows that validated and were created.</param>
/// <param name="RejectedRows">Each rejected row with its 1-based row number and reason.</param>
public sealed record BulkImportResult(
    int TotalRows,
    int CreatedCount,
    IReadOnlyList<RejectedRow> RejectedRows);

/// <summary>A single rejected import row (Req 21.2): its 1-based data-row number and why it was skipped.</summary>
/// <param name="RowNumber">1-based position among the data rows (header excluded).</param>
/// <param name="Reason">Human-readable reason the row was skipped.</param>
public sealed record RejectedRow(int RowNumber, string Reason);

/// <summary>A produced export (Req 21.3): the CSV bytes, content type and suggested download file name.</summary>
/// <param name="Content">The CSV payload.</param>
/// <param name="ContentType">Always <c>text/csv</c>.</param>
/// <param name="FileName">Suggested download file name.</param>
/// <param name="RowCount">Number of unit rows written (excludes the header).</param>
public sealed record BulkExportResult(
    byte[] Content,
    string ContentType,
    string FileName,
    int RowCount);

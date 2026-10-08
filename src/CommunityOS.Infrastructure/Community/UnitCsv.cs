using System.Globalization;
using System.Text;

namespace CommunityOS.Infrastructure.Community;

/// <summary>
/// Minimal, dependency-free CSV reader/writer for the Unit bulk import/export (Task 13.1). CSV is
/// the common admin spreadsheet format; a tiny hand-rolled RFC-4180-style parser avoids pulling in a
/// heavy third-party CSV library for the handful of columns a Unit needs (steering: no new
/// dependency when the standard library covers it).
///
/// <para>Supported: comma separator, <c>"</c>-quoted fields, embedded commas/quotes (<c>""</c>
/// escape) and newlines inside quotes, and CRLF or LF line endings. Rows are read lazily from the
/// in-memory text (the caller has already bounded the input by the row cap, so the whole file in
/// memory is acceptable here). This is intentionally not a general CSV engine — it reads/writes the
/// fixed Unit column set only.</para>
///
/// <para>ponytail: fixed column schema, in-memory parse bounded by the import row cap. If Units ever
/// need streaming import of files far beyond the cap, swap this for a streaming reader — the row cap
/// is the upgrade trigger.</para>
/// </summary>
internal static class UnitCsv
{
    /// <summary>Export column order. Import tolerates a header in any order (mapped by name).</summary>
    public static readonly string[] ExportHeader =
        ["id", "communityId", "unitNumber", "unitType", "area", "floor", "bedrooms", "unitStatus"];

    /// <summary>The columns an import row understands (lower-cased, header-mapped).</summary>
    public static readonly string[] ImportColumns =
        ["unitnumber", "unittype", "area", "floor", "bedrooms", "unitstatus"];

    /// <summary>
    /// Parse CSV text into records keyed by the (lower-cased) header column name. The first
    /// non-empty logical line is treated as the header. Returns an empty list when there is no data
    /// row. A record only exposes the columns present in the header; missing columns read as null.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> Parse(string content)
    {
        var rows = ParseRows(content);
        if (rows.Count == 0)
        {
            return [];
        }

        // Header: lower-case + trim so "UnitNumber"/"unitnumber" map the same.
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();

        var records = new List<IReadOnlyDictionary<string, string?>>(rows.Count - 1);
        for (var i = 1; i < rows.Count; i++)
        {
            var fields = rows[i];
            var record = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < header.Length; c++)
            {
                record[header[c]] = c < fields.Count ? fields[c] : null;
            }

            records.Add(record);
        }

        return records;
    }

    /// <summary>Write the export header + one line per unit row to a UTF-8 CSV string.</summary>
    public static string Write(IEnumerable<string?[]> dataRows)
    {
        var sb = new StringBuilder();
        AppendLine(sb, ExportHeader);
        foreach (var row in dataRows)
        {
            AppendLine(sb, row);
        }

        return sb.ToString();
    }

    /// <summary>Invariant-culture formatting for the numeric export columns so a locale never changes the output.</summary>
    public static string? Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    public static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static void AppendLine(StringBuilder sb, IReadOnlyList<string?> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(Escape(fields[i]));
        }

        sb.Append("\r\n");
    }

    private static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        // Quote only when needed (comma, quote, CR or LF present); double any embedded quote.
        if (field.IndexOfAny(['"', ',', '\r', '\n']) < 0)
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    /// <summary>
    /// Split CSV text into logical rows of fields, honouring quotes (embedded commas/newlines) and
    /// the <c>""</c> escape. Blank lines are skipped so a trailing newline does not add an empty row.
    /// </summary>
    private static List<List<string>> ParseRows(string content)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(content))
        {
            return rows;
        }

        var current = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldHadContent = false;

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    // Doubled quote inside a quoted field => literal quote.
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    fieldHadContent = true;
                    break;
                case ',':
                    current.Add(field.ToString());
                    field.Clear();
                    fieldHadContent = true;
                    break;
                case '\r':
                    break; // handled with \n (CRLF) or ignored (lone CR)
                case '\n':
                    EndRow(rows, current, field, fieldHadContent);
                    current = [];
                    field = new StringBuilder();
                    fieldHadContent = false;
                    break;
                default:
                    field.Append(ch);
                    fieldHadContent = true;
                    break;
            }
        }

        // Final row without a trailing newline.
        EndRow(rows, current, field, fieldHadContent);
        return rows;
    }

    private static void EndRow(List<List<string>> rows, List<string> current, StringBuilder field, bool fieldHadContent)
    {
        // A line that produced no fields and no content is blank — skip it.
        if (current.Count == 0 && field.Length == 0 && !fieldHadContent)
        {
            return;
        }

        current.Add(field.ToString());
        rows.Add(current);
    }
}

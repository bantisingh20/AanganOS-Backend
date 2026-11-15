using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Persistence;

public static class DatabaseMigrationRunner
{
    private static readonly Regex GoRegex = new(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static async Task RunPendingMigrationsAsync(
        AppDbContext db,
        ILogger logger,
        string? scriptsDirectory = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Ensure __DatabaseMigrations tracking table exists
        await EnsureTrackingTableExistsAsync(db, cancellationToken);

        // 2. Resolve scripts directory
        var scriptsDir = ResolveScriptsDirectory(scriptsDirectory);
        if (scriptsDir is null || !Directory.Exists(scriptsDir))
        {
            logger.LogWarning("[DatabaseMigrations] Scripts directory not found. Looked in: {Path}", scriptsDir);
            return;
        }

        // 3. Get all .sql files ordered by name
        var sqlFiles = Directory.GetFiles(scriptsDir, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sqlFiles.Length == 0)
        {
            logger.LogInformation("[DatabaseMigrations] No SQL migration files found in {Path}.", scriptsDir);
            return;
        }

        // 4. Query already applied scripts
        var appliedScripts = await GetAppliedScriptsAsync(db, cancellationToken);

        var pendingFiles = sqlFiles
            .Where(f => !appliedScripts.Contains(Path.GetFileName(f)))
            .ToArray();

        logger.LogInformation(
            "[DatabaseMigrations] Found {Total} migration file(s): {Applied} applied, {Pending} pending.",
            sqlFiles.Length,
            appliedScripts.Count,
            pendingFiles.Length);

        if (pendingFiles.Length == 0)
        {
            return;
        }

        // 5. Execute pending migrations one by one
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        foreach (var file in pendingFiles)
        {
            var fileName = Path.GetFileName(file);
            logger.LogInformation("[DatabaseMigrations] Applying pending migration: {FileName}...", fileName);

            var sql = await File.ReadAllTextAsync(file, cancellationToken);
            var batches = GoRegex.Split(sql)
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .ToArray();

            var sw = Stopwatch.StartNew();
            var success = false;

            try
            {
                using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
                foreach (var batch in batches)
                {
                    await db.Database.ExecuteSqlRawAsync(batch, cancellationToken);
                }

                sw.Stop();
                success = true;

                await RecordMigrationAsync(db, fileName, cancellationToken);
                await tx.CommitAsync(cancellationToken);

                logger.LogInformation(
                    "[DatabaseMigrations] Successfully applied {FileName} in {Duration}ms.",
                    fileName,
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogError(
                    ex,
                    "[DatabaseMigrations] FAILED to apply {FileName} after {Duration}ms: {Message}",
                    fileName,
                    sw.ElapsedMilliseconds,
                    ex.Message);

                throw;
            }
        }
    }

    private static async Task EnsureTrackingTableExistsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '__DatabaseMigrations')
            BEGIN
                CREATE TABLE dbo.__DatabaseMigrations (
                    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    ScriptName NVARCHAR(255) NOT NULL UNIQUE,
                    AppliedAtUtc DATETIME2 NOT NULL
                );
            END;
            """;

        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private static async Task<HashSet<string>> GetAppliedScriptsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var conn = db.Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ScriptName FROM dbo.__DatabaseMigrations;";

        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }

    private static async Task RecordMigrationAsync(
        AppDbContext db,
        string scriptName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.__DatabaseMigrations WHERE ScriptName = @ScriptName)
            BEGIN
                INSERT INTO dbo.__DatabaseMigrations (ScriptName, AppliedAtUtc)
                VALUES (@ScriptName, SYSUTCDATETIME());
            END;
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            new[]
            {
                new SqlParameter("@ScriptName", scriptName),
            },
            cancellationToken);
    }

    private static string? ResolveScriptsDirectory(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && Directory.Exists(preferred))
        {
            return preferred;
        }

        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 7; i++)
        {
            var candidate = Path.Combine(current, "scripts", "sql");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        return null;
    }
}

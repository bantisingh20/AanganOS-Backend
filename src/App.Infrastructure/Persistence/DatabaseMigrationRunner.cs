using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Persistence;

public static class DatabaseMigrationRunner
{
    private static readonly Regex GoRegex = new(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Runs schema sync + pending SQL scripts under a database-wide lock (sp_getapplock), so when
    /// several API instances start together only one migrates; the others wait, then find nothing
    /// pending. The lock is owned by this connection's session and released in <c>finally</c>.
    /// </summary>
    public static async Task RunPendingMigrationsAsync(
        AppDbContext db,
        ILogger logger,
        string? scriptsDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        // Wait up to 10 minutes for another instance's migration to finish.
        await db.Database.ExecuteSqlRawAsync(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'AanganOS.DbMigrations', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 600000; " +
            "IF @r < 0 THROW 51000, 'Could not acquire the database migration lock.', 1;",
            cancellationToken);
        try
        {
            await RunPendingMigrationsLockedAsync(db, logger, scriptsDirectory, cancellationToken);
        }
        finally
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "EXEC sp_releaseapplock @Resource = N'AanganOS.DbMigrations', @LockOwner = 'Session';",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[DatabaseMigrations] Could not release the migration lock (it is freed when the connection closes).");
            }
        }
    }

    private static async Task RunPendingMigrationsLockedAsync(
        AppDbContext db,
        ILogger logger,
        string? scriptsDirectory,
        CancellationToken cancellationToken)
    {
        // 0. Automatically sync schema (tables, missing columns, indexes) from C# Entities
        await AutoSyncSchemaAsync(db, logger, cancellationToken);

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

            try
            {
                using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
                foreach (var batch in batches)
                {
                    // ExecuteSqlRaw treats { } as parameter placeholders ({0}); scripts pass no
                    // parameters, so escape them or a brace in a comment/string breaks startup.
                    await db.Database.ExecuteSqlRawAsync(EscapeBraces(batch), cancellationToken);
                }

                sw.Stop();

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

    public static async Task AutoSyncSchemaAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entityTypes = db.Model.GetEntityTypes();
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }

            foreach (var entityType in entityTypes)
            {
                var tableName = entityType.GetTableName();
                if (string.IsNullOrWhiteSpace(tableName)) continue;

                var schema = entityType.GetSchema() ?? "dbo";
                var storeObject = StoreObjectIdentifier.Table(tableName, schema);

                // 1. Check if table exists in SQL Server
                var tableExistsSql = $"SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE t.name = '{tableName}' AND s.name = '{schema}';";
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = tableExistsSql;
                    var exists = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (exists is null)
                    {
                        logger.LogInformation("[AutoSyncSchema] Table [{Schema}].[{Table}] does not exist. Creating table...", schema, tableName);
                        var columnDefs = new List<string>();
                        foreach (var prop in entityType.GetProperties())
                        {
                            var colName = prop.GetColumnName(storeObject) ?? prop.Name;
                            var colType = prop.GetColumnType(storeObject) ?? "nvarchar(max)";
                            var nullable = prop.IsColumnNullable(storeObject) ? "NULL" : "NOT NULL";
                            columnDefs.Add($"[{colName}] {colType} {nullable}");
                        }

                        var pk = entityType.FindPrimaryKey();
                        if (pk != null && pk.Properties.Count > 0)
                        {
                            var pkCols = string.Join(", ", pk.Properties.Select(p => $"[{p.GetColumnName(storeObject) ?? p.Name}]"));
                            columnDefs.Add($"CONSTRAINT [PK_{tableName}] PRIMARY KEY ({pkCols})");
                        }

                        var createTableSql = $"CREATE TABLE [{schema}].[{tableName}] (\n  {string.Join(",\n  ", columnDefs)}\n);";
                        await db.Database.ExecuteSqlRawAsync(createTableSql, cancellationToken);
                        logger.LogInformation("[AutoSyncSchema] Created table [{Schema}].[{Table}].", schema, tableName);
                        continue;
                    }
                }

                // 2. Table exists: check and add any missing columns
                foreach (var prop in entityType.GetProperties())
                {
                    var colName = prop.GetColumnName(storeObject) ?? prop.Name;
                    var colType = prop.GetColumnType(storeObject)
                        ?? (prop.ClrType == typeof(Guid) || prop.ClrType == typeof(Guid?) ? "uniqueidentifier"
                            : prop.ClrType == typeof(string) ? "nvarchar(max)"
                            : prop.ClrType == typeof(int) || prop.ClrType == typeof(int?) ? "int"
                            : prop.ClrType == typeof(bool) || prop.ClrType == typeof(bool?) ? "bit"
                            : prop.ClrType == typeof(DateTime) || prop.ClrType == typeof(DateTime?) ? "datetime2"
                            : "nvarchar(max)");

                    try
                    {
                        var checkColSql = $"SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('[{schema}].[{tableName}]') AND name = '{colName}';";
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = checkColSql;
                        var colExists = await cmd.ExecuteScalarAsync(cancellationToken);
                        if (colExists is null)
                        {
                            logger.LogInformation("[AutoSyncSchema] Adding missing column [{Table}].[{Column}] ({Type})...", tableName, colName, colType);
                            cmd.CommandText = $"ALTER TABLE [{schema}].[{tableName}] ADD [{colName}] {colType} NULL;";
                            await cmd.ExecuteNonQueryAsync(cancellationToken);
                            logger.LogInformation("[AutoSyncSchema] Successfully added column [{Table}].[{Column}].", tableName, colName);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[AutoSyncSchema] Could not add column [{Table}].[{Column}]: {Message}", tableName, colName, ex.Message);
                    }
                }

                // 3. Check and add any missing indexes
                foreach (var index in entityType.GetIndexes())
                {
                    var indexName = index.GetDatabaseName(storeObject) ?? index.GetDatabaseName();
                    if (string.IsNullOrWhiteSpace(indexName)) continue;

                    try
                    {
                        var checkIndexSql = $"SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('[{schema}].[{tableName}]');";
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = checkIndexSql;
                        var indexExists = await cmd.ExecuteScalarAsync(cancellationToken);
                        if (indexExists is null)
                        {
                            var indexCols = string.Join(", ", index.Properties.Select(p => $"[{p.GetColumnName(storeObject) ?? p.Name}]"));
                            var unique = index.IsUnique ? "UNIQUE" : "";
                            cmd.CommandText = $"CREATE {unique} INDEX [{indexName}] ON [{schema}].[{tableName}] ({indexCols});";
                            logger.LogInformation("[AutoSyncSchema] Creating missing index [{Index}] on [{Table}]...", indexName, tableName);
                            await cmd.ExecuteNonQueryAsync(cancellationToken);
                            logger.LogInformation("[AutoSyncSchema] Created index [{Index}] on [{Table}].", indexName, tableName);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[AutoSyncSchema] Failed to create index [{Index}] on [{Table}]: {Message}", indexName, tableName, ex.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AutoSyncSchema] Schema auto-sync encountered an error: {Message}", ex.Message);
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

    /// <summary>Doubles { and } so ExecuteSqlRaw sends the script text unchanged.</summary>
    internal static string EscapeBraces(string sql) => sql.Replace("{", "{{").Replace("}", "}}");

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

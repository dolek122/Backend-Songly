using DbUp;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Songly.Migrations;

/// <summary>
/// Brings the database schema up to date with the SQL scripts in <c>Scripts/</c>.
/// </summary>
/// <remarks>
/// <para>
/// The scripts are the original backend's Flyway migrations, copied verbatim (docs/adr/0003):
/// the schema is the contract with the data, so it is not re-expressed through an ORM. Every
/// script runs in its own transaction, and a failed one stops the application from starting —
/// an instance running against a half-migrated schema is worse than one that does not start.
/// </para>
/// <para>
/// The run is guarded by a Postgres advisory lock (what Flyway does too), so two instances
/// starting at the same time do not apply the same script twice.
/// </para>
/// </remarks>
public static class DatabaseMigrator
{
    // Arbitrary, but fixed: every instance must ask for the same lock.
    private const long AdvisoryLockKey = 0x536F6E676C79; // "Songly"

    public static void Migrate(string connectionString, ILoggerFactory loggerFactory)
    {
        using var lockConnection = new NpgsqlConnection(connectionString);
        lockConnection.Open();
        Execute(lockConnection, "SELECT pg_advisory_lock(@key)");

        try
        {
            var result = DeployChanges.To
                .PostgresqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly, name => name.EndsWith(".sql", StringComparison.Ordinal))
                .WithScriptNameComparer(new MigrationVersionComparer())
                .WithTransactionPerScript()
                .WithVariablesDisabled()
                .JournalToPostgresqlTable("public", "schema_versions")
                .LogTo(loggerFactory)
                .Build()
                .PerformUpgrade();

            if (!result.Successful)
            {
                throw new InvalidOperationException($"Database migration failed at script {result.ErrorScript?.Name}", result.Error);
            }
        }
        finally
        {
            Execute(lockConnection, "SELECT pg_advisory_unlock(@key)");
        }
    }

    private static void Execute(NpgsqlConnection connection, string sql)
    {
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        command.ExecuteNonQuery();
    }
}

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace RowadUmrahSystem.Web.Data
{
    /// <summary>
    /// Runs before the migrations. SchemaColumns adds model columns that a database lacks, so a
    /// database that ran newer code before its migration existed already has some of the columns
    /// (or tables, or indexes) a pending migration creates, and SQLite stops on "duplicate column
    /// name". For each pending migration that is partly present, this applies only the missing
    /// operations and records the migration as applied, in one transaction, after copying the
    /// database file next to it. Ordinary pending migrations are left to MigrateAsync.
    /// </summary>
    public static class MigrationAlignment
    {
        public static async Task AlignAsync(ApplicationDbContext context, ILogger logger)
        {
            var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count == 0) return;

            var assembly = context.GetService<IMigrationsAssembly>();
            var generator = context.GetService<IMigrationsSqlGenerator>();
            var history = context.GetService<IHistoryRepository>();
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
            var backedUp = false;

            foreach (var id in pending)
            {
                var migration = assembly.CreateMigration(assembly.Migrations[id], context.Database.ProviderName!);
                var missing = new List<MigrationOperation>();
                var present = 0;
                foreach (var operation in migration.UpOperations)
                {
                    if (await ExistsAsync(connection, operation)) present++;
                    else missing.Add(operation);
                }
                if (present == 0) break; // a normal migration: MigrateAsync applies it and the ones after

                if (!backedUp)
                {
                    BackUp(connection, logger);
                    backedUp = true;
                }

                await using var transaction = await connection.BeginTransactionAsync();
                foreach (var command in generator.Generate(missing))
                {
                    await ExecuteAsync(connection, transaction, command.CommandText);
                }
                await ExecuteAsync(connection, transaction, history.GetInsertScript(new HistoryRow(id, ProductInfo.GetVersion())));
                await transaction.CommitAsync();
                logger.LogWarning("Schema repair: migration {Migration} was partly in the database; applied the {Missing} missing operations and skipped {Present}",
                    id, missing.Count, present);
            }
        }

        private static async Task<bool> ExistsAsync(DbConnection connection, MigrationOperation operation) => operation switch
        {
            AddColumnOperation column => await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_table_info($a) WHERE name = $b COLLATE NOCASE", column.Table, column.Name) > 0,
            CreateTableOperation table => await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $a COLLATE NOCASE", table.Name) > 0,
            CreateIndexOperation index => await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $a COLLATE NOCASE", index.Name) > 0,
            _ => false
        };

        private static async Task<long> ScalarAsync(DbConnection connection, string sql, string a, string? b = null)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "$a", a);
            if (b != null) AddParameter(command, "$b", b);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        private static void AddParameter(DbCommand command, string name, string value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private static async Task ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        private static void BackUp(DbConnection connection, ILogger logger)
        {
            var path = connection.DataSource;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var copy = $"{path}.before-repair-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(path, copy);
            logger.LogWarning("Schema repair: database copied to {Copy} before the repair", copy);
        }
    }
}

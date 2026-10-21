using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RowadUmrahSystem.Web.Data
{
    /// <summary>
    /// Adds every column the EF model expects but the SQLite database lacks.
    /// Some schema changes were hand-written migrations without a [Migration] attribute
    /// (AddTripStatus, AddDashboardNotificationPermissions, AddTravelerProfileFields,
    /// AddTravelerDocumentsReviewedState): EF never applied them, so databases differ
    /// (production misses some columns, older local copies others). SQLite has no
    /// "ADD COLUMN IF NOT EXISTS", so the check runs here after the migrations.
    /// Existing rows get the column default; nothing is dropped or rewritten.
    /// </summary>
    public static class SchemaColumns
    {
        /// <summary>Defaults that differ from the CLR default for that column.</summary>
        private static readonly Dictionary<string, string> DefaultOverrides = new()
        {
            ["Trips.Status"] = "'pending'"
        };

        public static async Task EnsureAsync(ApplicationDbContext context, ILogger logger)
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            foreach (var entityType in context.Model.GetEntityTypes())
            {
                var table = entityType.GetTableName();
                if (table == null || entityType.GetViewName() != null) continue;

                var existing = await ReadColumnsAsync(connection, table);
                if (existing.Count == 0) continue; // table itself is missing: migrations own tables

                var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
                foreach (var property in entityType.GetProperties())
                {
                    var column = property.GetColumnName(store);
                    if (column == null || existing.Contains(column)) continue;

                    var type = property.GetColumnType(store);
                    var sql = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {type}";
                    if (!property.IsNullable)
                    {
                        sql += " NOT NULL DEFAULT " + (DefaultOverrides.GetValueOrDefault($"{table}.{column}") ?? DefaultLiteral(property.ClrType));
                    }

                    await context.Database.ExecuteSqlRawAsync(sql);
                    existing.Add(column);
                    logger.LogWarning("Schema repair: added missing column {Table}.{Column}", table, column);
                }
            }
        }

        private static async Task<HashSet<string>> ReadColumnsAsync(System.Data.Common.DbConnection connection, string table)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM pragma_table_info($table)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$table";
            parameter.Value = table;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
            return columns;
        }

        private static string DefaultLiteral(Type clrType)
        {
            var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
            if (type == typeof(string)) return "''";
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "'0001-01-01 00:00:00'";
            if (type == typeof(Guid)) return "'00000000-0000-0000-0000-000000000000'";
            return "0"; // bool, numbers, enums
        }
    }
}

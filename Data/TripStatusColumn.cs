using Microsoft.EntityFrameworkCore;

namespace RowadUmrahSystem.Web.Data
{
    /// <summary>
    /// Adds Trips.Status when the database does not have it yet. No migration owns this column:
    /// production already had it while fresh databases did not, and SQLite cannot add a column
    /// conditionally inside a migration.
    /// </summary>
    public static class TripStatusColumn
    {
        public static async Task EnsureAsync(ApplicationDbContext context)
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Trips') WHERE name = 'Status'";
                if (Convert.ToInt64(await command.ExecuteScalarAsync()) > 0) return;
            }

            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Trips\" ADD COLUMN \"Status\" TEXT NOT NULL DEFAULT 'pending'");
        }
    }
}

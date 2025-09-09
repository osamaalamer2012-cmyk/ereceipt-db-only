using System.Data;
using EReceiptAllInOne.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EReceiptAllInOne.Services;

public interface IRateLimitService
{
    Task<bool> HitAsync(string key, int limit, TimeSpan window);
}

public class SqlRateLimitService : IRateLimitService
{
    private readonly AppDbContext _db;
    public SqlRateLimitService(AppDbContext db) { _db = db; }

    public async Task<bool> HitAsync(string key, int limit, TimeSpan window)
    {
        // Compute the window end (ceil to window)
        var now = DateTimeOffset.UtcNow;
        var ticks = window.Ticks;
        var winEndUtc = new DateTimeOffset(
            new DateTime(((now.UtcDateTime.Ticks + ticks - 1) / ticks) * ticks, DateTimeKind.Utc)
        );

        // Postgres upsert that returns the new count
        const string sql = @"
INSERT INTO ""RateLimits"" (""Key"", ""WindowEndUtc"", ""Count"")
VALUES (@k, @w, 1)
ON CONFLICT (""Key"", ""WindowEndUtc"")
DO UPDATE SET ""Count"" = ""RateLimits"".""Count"" + 1
RETURNING ""Count"";";

        // IMPORTANT: open our own Npgsql connection instead of using EF's connection instance
        var cs = _db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
        {
            // Fallback to current connection object's string if needed
            cs = _db.Database.GetDbConnection().ConnectionString;
        }

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("@k", NpgsqlTypes.NpgsqlDbType.Text) { Value = key });
        cmd.Parameters.Add(new NpgsqlParameter("@w", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = winEndUtc.UtcDateTime });

        var result = await cmd.ExecuteScalarAsync();
        var count = Convert.ToInt32(result);

        return count <= limit;
    }
}

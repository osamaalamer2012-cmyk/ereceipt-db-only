using System.Data;
using EReceiptAllInOne.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

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
        var now = DateTimeOffset.UtcNow;
        // ceil to window end
        var ticks = window.Ticks;
        var winEndUtc = new DateTimeOffset(new DateTime(((now.UtcDateTime.Ticks + ticks - 1) / ticks) * ticks, DateTimeKind.Utc));

        var sql = @"
INSERT INTO ""RateLimits"" (""Key"", ""WindowEndUtc"", ""Count"")
VALUES (@p0, @p1, 1)
ON CONFLICT (""Key"", ""WindowEndUtc"")
DO UPDATE SET ""Count"" = ""RateLimits"".""Count"" + 1
RETURNING ""Count"";";

        await using var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var p0 = cmd.CreateParameter(); p0.ParameterName = "@p0"; p0.Value = key; cmd.Parameters.Add(p0);
        var p1 = cmd.CreateParameter(); p1.ParameterName = "@p1"; p1.Value = winEndUtc; cmd.Parameters.Add(p1);

        var result = await cmd.ExecuteScalarAsync();
        var count = Convert.ToInt32(result);
        return count <= limit;
    }
}

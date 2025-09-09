using EReceiptAllInOne.Data;
using Microsoft.EntityFrameworkCore;

namespace EReceiptAllInOne.OtpStore;

public class SqlOtpStore : IOtpStore
{
    private readonly AppDbContext _db;
    public SqlOtpStore(AppDbContext db) { _db = db; }

    public async Task SetAsync(string key, string code, TimeSpan ttl)
    {
        var rec = await _db.OtpCodes.FirstOrDefaultAsync(x => x.Token == key);
        var exp = DateTimeOffset.UtcNow.Add(ttl);
        if (rec is null)
        {
            _db.OtpCodes.Add(new OtpCodeEntity { Token = key, Code = code, ExpiresAtUtc = exp });
        }
        else
        {
            rec.Code = code;
            rec.ExpiresAtUtc = exp;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? code)> GetAsync(string key)
    {
        var rec = await _db.OtpCodes.AsNoTracking().FirstOrDefaultAsync(x => x.Token == key);
        if (rec is null || rec.ExpiresAtUtc <= DateTimeOffset.UtcNow) return (false, null);
        return (true, rec.Code);
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var rec = await _db.OtpCodes.FirstOrDefaultAsync(x => x.Token == key);
        if (rec is null) return false;
        _db.OtpCodes.Remove(rec);
        await _db.SaveChangesAsync();
        return true;
    }
}

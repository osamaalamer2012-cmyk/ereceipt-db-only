using EReceiptAllInOne.Data;
using Microsoft.EntityFrameworkCore;

namespace EReceiptAllInOne.Repositories.Impl;

public class SqlReceiptRepository : IReceiptRepository
{
    private readonly AppDbContext _db;
    public SqlReceiptRepository(AppDbContext db) { _db = db; }

    public async Task CreateAsync(ReceiptEntity r, CancellationToken ct = default)
    {
        _db.Receipts.Add(r);
        await _db.SaveChangesAsync(ct);
    }

    public Task<ReceiptEntity?> GetAsync(string id, CancellationToken ct = default)
        => _db.Receipts.AsNoTracking().FirstOrDefaultAsync(x => x.ReceiptId == id, ct);

    public async Task IncrementUsesAsync(string id, CancellationToken ct = default)
    {
        var r = await _db.Receipts.FirstOrDefaultAsync(x => x.ReceiptId == id, ct);
        if (r is null) return;
        r.Uses += 1;
        await _db.SaveChangesAsync(ct);
    }
}

public class SqlShortLinkRepository : IShortLinkRepository
{
    private readonly AppDbContext _db;
    public SqlShortLinkRepository(AppDbContext db) { _db = db; }

    public async Task CreateAsync(ShortLinkEntity s, CancellationToken ct = default)
    {
        _db.ShortLinks.Add(s);
        await _db.SaveChangesAsync(ct);
    }

    public Task<ShortLinkEntity?> GetAsync(string code, CancellationToken ct = default)
        => _db.ShortLinks.FirstOrDefaultAsync(x => x.Code == code, ct);

    public async Task IncrementUsageAsync(string code, CancellationToken ct = default)
    {
        var s = await _db.ShortLinks.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (s is null) return;
        s.Usage += 1;
        await _db.SaveChangesAsync(ct);
    }
}

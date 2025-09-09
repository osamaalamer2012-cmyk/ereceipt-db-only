using EReceiptAllInOne.Data;

namespace EReceiptAllInOne.Repositories;

public interface IReceiptRepository
{
    Task CreateAsync(ReceiptEntity r, CancellationToken ct = default);
    Task<ReceiptEntity?> GetAsync(string id, CancellationToken ct = default);
    Task IncrementUsesAsync(string id, CancellationToken ct = default);
}

public interface IShortLinkRepository
{
    Task CreateAsync(ShortLinkEntity s, CancellationToken ct = default);
    Task<ShortLinkEntity?> GetAsync(string code, CancellationToken ct = default);
    Task IncrementUsageAsync(string code, CancellationToken ct = default);
}

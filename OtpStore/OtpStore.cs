namespace EReceiptAllInOne.OtpStore;

public interface IOtpStore
{
    Task SetAsync(string key, string code, TimeSpan ttl);
    Task<(bool ok, string? code)> GetAsync(string key);
    Task<bool> DeleteAsync(string key);
}

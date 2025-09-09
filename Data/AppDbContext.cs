using Microsoft.EntityFrameworkCore;

namespace EReceiptAllInOne.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ReceiptEntity> Receipts => Set<ReceiptEntity>();
    public DbSet<ShortLinkEntity> ShortLinks => Set<ShortLinkEntity>();
    public DbSet<RateLimitEntity> RateLimits => Set<RateLimitEntity>();
    public DbSet<OtpCodeEntity> OtpCodes => Set<OtpCodeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReceiptEntity>().HasIndex(x => x.TxnId);
        modelBuilder.Entity<ShortLinkEntity>().HasIndex(x => x.ExpiresAt);
        modelBuilder.Entity<RateLimitEntity>().HasIndex(x => new { x.Key, x.WindowEndUtc }).IsUnique();
        modelBuilder.Entity<OtpCodeEntity>().HasIndex(x => x.Token).IsUnique();
    }
}

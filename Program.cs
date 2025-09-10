using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using EReceiptAllInOne.Data;
using EReceiptAllInOne.Models;
using EReceiptAllInOne.OtpStore;
using EReceiptAllInOne.Repositories;
using EReceiptAllInOne.Repositories.Impl;
using EReceiptAllInOne.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EReceiptAllInOne;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var demoMode   = builder.Configuration.GetValue<bool>("Demo", true);
        var shortOpts  = builder.Configuration.GetSection("Shortener").Get<ShortenerOptions>() ?? new ShortenerOptions();
        var jwtOpts    = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        
        var storage    = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
        var rlOptions  = builder.Configuration.GetSection("RateLimit").Get<RateLimitOptions>() ?? new RateLimitOptions();

        var adminKey = builder.Configuration["AdminKey"]; // required for /admin/db endpoints

        builder.Services.AddSingleton(jwtOpts);   // <-- add this
        // DB provider
        builder.Services.AddDbContext<AppDbContext>(opt =>
        {
            if (string.Equals(storage.SqlProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
                opt.UseNpgsql(storage.ConnectionStrings.Postgres);
            else if (string.Equals(storage.SqlProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
                opt.UseSqlServer(storage.ConnectionStrings.SqlServer);
            else
                opt.UseSqlite(storage.ConnectionStrings.Sqlite);
        });

        builder.Services.AddScoped<IReceiptRepository, SqlReceiptRepository>();
        builder.Services.AddScoped<IShortLinkRepository, SqlShortLinkRepository>();
        builder.Services.AddScoped<IOtpStore, SqlOtpStore>();
        builder.Services.AddScoped<IRateLimitService, SqlRateLimitService>();

        // JWT with rotation
        builder.Services.AddSingleton(new JwtKeyRing(jwtOpts));
        builder.Services.AddSingleton<JwtService>();

        // RateLimiter middleware (per-IP policies)
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("otp-ip", context =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, rlOptions.Otp.PerIpPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            options.AddPolicy("shorten-ip", context =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, rlOptions.Shorten.PerIpPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            options.RejectionStatusCode = 429;
        });

        var app = builder.Build();
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        // If the request is to our JSON APIs, return JSON on errors
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/tcrm/") || path.StartsWith("/api/") || path.StartsWith("/s/"))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Unexpected server error" });
        }
        else
        {
            // Non-API paths fall back to a lightweight HTML error
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("<h3>Unexpected server error</h3>");
        }
    });
});

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRateLimiter();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // Ensure DB
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        }

        // Root
        app.MapMethods("/", new[] { "GET", "HEAD" }, (IWebHostEnvironment env) =>
        {
            var p = Path.Combine(env.WebRootPath ?? "", "index.html");
            return File.Exists(p) ? Results.File(p, "text/html")
                                  : Results.Content("<h1>e-Receipt</h1><p>Missing wwwroot/index.html</p>", "text/html");
        });
// ---- Admin: DB viewers (read-only). Send header: X-Admin-Key: <AdminKey>
app.MapGet("/admin/db/receipts", async (int? take, AppDbContext db, HttpContext ctx) =>
{
    if (!IsAdmin(ctx, adminKey)) return Results.Unauthorized();
    var limit = take is > 0 and <= 500 ? take.Value : 50;
    var rows = await db.Receipts.AsNoTracking()
        .OrderByDescending(r => r.CreatedAt)
        .Take(limit)
        .Select(r => new {
            r.ReceiptId, r.TxnId, r.Msisdn, r.Amount, r.Currency,
            r.MaxUses, r.Uses, r.ExpiresAt, r.CreatedAt
        })
        .ToListAsync();
    return Results.Ok(rows);
});

app.MapGet("/admin/db/shortlinks", async (int? take, AppDbContext db, HttpContext ctx) =>
{
    if (!IsAdmin(ctx, adminKey)) return Results.Unauthorized();
    var limit = take is > 0 and <= 500 ? take.Value : 50;
    var rows = await db.ShortLinks.AsNoTracking()
        .OrderByDescending(s => s.CreatedAt)
        .Take(limit)
        .Select(s => new {
            s.Code, s.LongUrl, s.Usage, s.UsageMax, s.ExpiresAt, s.CreatedAt
        })
        .ToListAsync();
    return Results.Ok(rows);
});

app.MapGet("/admin/db/otpcodes", async (int? take, AppDbContext db, HttpContext ctx) =>
{
    if (!IsAdmin(ctx, adminKey)) return Results.Unauthorized();
    var limit = take is > 0 and <= 500 ? take.Value : 50;
    var rows = await db.OtpCodes.AsNoTracking()
        .OrderByDescending(o => o.ExpiresAtUtc)
        .Take(limit)
        .Select(o => new {
            o.Token,
            Code = "***",               // mask the OTP
            o.ExpiresAtUtc
        })
        .ToListAsync();
    return Results.Ok(rows);
});

app.MapGet("/admin/db/ratelimits", async (int? take, AppDbContext db, HttpContext ctx) =>
{
    if (!IsAdmin(ctx, adminKey)) return Results.Unauthorized();
    var limit = take is > 0 and <= 500 ? take.Value : 50;
    var rows = await db.RateLimits.AsNoTracking()
        .OrderByDescending(r => r.WindowEndUtc)
        .Take(limit)
        .Select(r => new { r.Key, r.WindowEndUtc, r.Count })
        .ToListAsync();
    return Results.Ok(rows);
});

        // Shortener
        app.MapPost("/api/shorten", async (ShortenRequest req, IShortLinkRepository repo, IRateLimitService rl) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.LongUrl))
                return Results.BadRequest(new { error = "Missing longUrl" });

            var ok = await rl.HitAsync($"rl:short:anon:{DateTimeOffset.UtcNow:yyyyMMddHH}", Math.Max(1, rlOptions.Shorten.PerKeyPerHour), TimeSpan.FromHours(1));
            if (!ok) return Results.StatusCode(429);

            var code = GenerateCode(shortOpts.CodeLength <= 0 ? 7 : shortOpts.CodeLength);
            var expires = DateTimeOffset.UtcNow.AddHours(req.ExpiryHours ?? shortOpts.DefaultTtlHours);
            var usageMax = req.UsageMax ?? shortOpts.DefaultUsageMax;

            var s = new ShortLinkEntity
            {
                Code = code,
                LongUrl = req.LongUrl,
                Usage = 0,
                UsageMax = usageMax,
                ExpiresAt = expires,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await repo.CreateAsync(s);

            var baseUrl = (shortOpts.ShortBaseUrl ?? "").TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "http://localhost:8080";
            var shortUrl = $"{baseUrl}/s/{code}";
            return Results.Ok(new ShortenResponse { shortUrl = shortUrl, code = code, expiresAt = expires });
        }).RequireRateLimiting("shorten-ip");

        app.MapGet("/s/{code}", async (string code, IShortLinkRepository repo) =>
        {
            var map = await repo.GetAsync(code);
            if (map is null) return Results.Content(HtmlError("Unknown code"), "text/html");
            if (map.ExpiresAt <= DateTimeOffset.UtcNow) return Results.Content(HtmlError("Link expired"), "text/html");
            if (map.Usage >= map.UsageMax) return Results.Content(HtmlError("Usage limit exceeded"), "text/html");

            await repo.IncrementUsageAsync(code);
            return Results.Redirect(map.LongUrl, false);
        });

        // Issue
        app.MapPost("/tcrm/issue", async (IssueRequest req, IReceiptRepository recRepo, IShortLinkRepository shortRepo, JwtService jwtService) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(req.TxnId) || string.IsNullOrWhiteSpace(req.Msisdn))
                return Results.BadRequest(new { error = "Missing fields" });

            var expiresAt = DateTimeOffset.UtcNow.AddHours(shortOpts.DefaultTtlHours <= 0 ? 48 : shortOpts.DefaultTtlHours);

            var rec = new ReceiptEntity
            {
                ReceiptId = Guid.NewGuid().ToString("N"),
                TxnId = req.TxnId,
                Msisdn = req.Msisdn,
                Amount = req.Amount,
                Currency = string.IsNullOrWhiteSpace(req.Currency) ? "USD" : req.Currency,
                ItemsJson = JsonSerializer.Serialize(req.Items ?? new List<ReceiptItem>()),
                ExpiresAt = expiresAt,
                MaxUses = shortOpts.DefaultUsageMax <= 0 ? 2 : shortOpts.DefaultUsageMax,
                Uses = 0,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await recRepo.CreateAsync(rec);

            var jti = Guid.NewGuid().ToString("N");
            var jwt = jwtService.CreateToken(jti, rec);

            var longUrl = $"{shortOpts.ViewBaseUrl}?token={jwt}";

            var code = GenerateCode(shortOpts.CodeLength <= 0 ? 7 : shortOpts.CodeLength);
            var s = new ShortLinkEntity
            {
                Code = code,
                LongUrl = longUrl,
                Usage = 0,
                UsageMax = rec.MaxUses,
                ExpiresAt = expiresAt,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await shortRepo.CreateAsync(s);

            var shortUrl = $"{shortOpts.ShortBaseUrl.TrimEnd('/')}/s/{code}";

            Console.WriteLine($"[DEMO SMS] to {rec.Msisdn}: {shortUrl}");
            return Results.Ok(new { receiptId = rec.ReceiptId, jwt, longUrl, shortUrl, expiresAt });
        });

        // OTP
        app.MapPost("/api/otp/send", async (OtpSendRequest req, IOtpStore otp, IReceiptRepository recRepo, JwtService jwtService, IRateLimitService rl) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Token))
                return Results.BadRequest(new { error = "Missing token" });

            var val = jwtService.Validate(req.Token);
            if (!val.ok) return Results.BadRequest(new { error = val.error });

            var jti = val.principal!.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value ?? "";
            var ok = await rl.HitAsync($"rl:otp:{jti}:{DateTimeOffset.UtcNow:yyyyMMddHHmm}", Math.Max(1, rlOptions.Otp.PerJtiPerMinute), TimeSpan.FromMinutes(1));
            if (!ok) return Results.StatusCode(429);

            var rid = val.principal!.FindFirst("rid")?.Value;
            if (string.IsNullOrWhiteSpace(rid)) return Results.BadRequest(new { error = "Invalid receipt" });
            var rec = await recRepo.GetAsync(rid!);
            if (rec is null) return Results.BadRequest(new { error = "Invalid receipt" });
            if (rec.ExpiresAt <= DateTimeOffset.UtcNow) return Results.BadRequest(new { error = "Link expired" });

            var code = new Random().Next(100000, 999999).ToString();
            await otp.SetAsync($"otp:{jti}", code, TimeSpan.FromMinutes(5));

            Console.WriteLine($"[DEMO OTP] to {rec.Msisdn}: {code}");
            return Results.Ok(new { otpDemo = demoMode ? code : "SENT" });
        }).RequireRateLimiting("otp-ip");

        app.MapPost("/api/otp/verify", async (OtpVerifyRequest req, IOtpStore otp, IReceiptRepository recRepo, JwtService jwtService) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.Code))
                return Results.BadRequest(new { error = "Missing fields" });

            var val = jwtService.Validate(req.Token);
            if (!val.ok) return Results.BadRequest(new { error = val.error });

            var rid = val.principal!.FindFirst("rid")?.Value;
            if (string.IsNullOrWhiteSpace(rid)) return Results.BadRequest(new { error = "Invalid receipt" });
            var rec = await recRepo.GetAsync(rid);
            if (rec is null) return Results.BadRequest(new { error = "Invalid receipt" });
            if (rec.ExpiresAt <= DateTimeOffset.UtcNow) return Results.BadRequest(new { error = "Link expired" });
            if (rec.Uses >= rec.MaxUses) return Results.BadRequest(new { error = "Usage limit exceeded" });

            var jti = val.principal!.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value ?? "";
            var (ok, stored) = await otp.GetAsync($"otp:{jti}");
            if (!ok) return Results.BadRequest(new { error = "OTP not issued/expired" });
            if (!string.Equals(stored, req.Code)) return Results.BadRequest(new { error = "Invalid code" });

            await otp.DeleteAsync($"otp:{jti}");
            await recRepo.IncrementUsesAsync(rid);

            return Results.Ok(new { receiptId = rec.ReceiptId });
        });

        app.MapGet("/api/receipt/{id}", async (string id, IReceiptRepository repo) =>
        {
            var rec = await repo.GetAsync(id);
            if (rec is null) return Results.NotFound(new { error = "Not found" });

            var items = System.Text.Json.JsonSerializer.Deserialize<List<ReceiptItem>>(rec.ItemsJson) ?? new();
            return Results.Ok(new {
                rec.ReceiptId, rec.TxnId, rec.Msisdn, rec.Amount, rec.Currency,
                items, rec.ExpiresAt, rec.MaxUses, rec.Uses, rec.CreatedAt
            });
        });

        var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
        app.Run($"http://0.0.0.0:{port}");
    }

    static string GenerateCode(int length)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        Span<char> chars = stackalloc char[length];
        for (int i = 0; i < length; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }

    static bool IsAdmin(HttpContext ctx, string? adminKey)
{
    if (string.IsNullOrEmpty(adminKey)) return false;
    var provided = ctx.Request.Headers["X-Admin-Key"].ToString();
    return !string.IsNullOrEmpty(provided) && provided == adminKey;
}


    static string HtmlError(string msg) =>
$@"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><title>Error</title>
<link rel='stylesheet' href='/style.css'></head><body><header><h1>Shortener</h1></header><main class='card'>
<h2>Cannot open link</h2><p>{System.Net.WebUtility.HtmlEncode(msg)}</p><p><a href='/'>Back</a></p></main></body></html>";
}

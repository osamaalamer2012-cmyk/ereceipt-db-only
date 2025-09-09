namespace EReceiptAllInOne.Models;

public record IssueRequest
{
    public string TxnId { get; init; } = default!;
    public string Msisdn { get; init; } = default!;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
    public List<ReceiptItem> Items { get; init; } = new();
}

public record ReceiptItem
{
    public string Sku { get; init; } = "";
    public string Name { get; init; } = "";
    public int Qty { get; init; }
    public decimal Price { get; init; }
}

public record ShortenRequest { public string LongUrl { get; init; } = default!; public int? ExpiryHours { get; init; } public int? UsageMax { get; init; } }
public record ShortenResponse { public string? shortUrl { get; init; } public string? code { get; init; } public DateTimeOffset? expiresAt { get; init; } }

public record OtpSendRequest { public string Token { get; init; } = default!; }
public record OtpVerifyRequest { public string Token { get; init; } = default!; public string Code { get; init; } = default!; }

public record ShortenerOptions
{
    public string ShortBaseUrl { get; set; } = "http://localhost:8080";
    public string ViewBaseUrl { get; set; } = "http://localhost:8080/view.html";
    public int CodeLength { get; set; } = 7;
    public int DefaultTtlHours { get; set; } = 48;
    public int DefaultUsageMax { get; set; } = 2;
}

public record JwtKey { public string Kid { get; set; } = default!; public string Secret { get; set; } = default!; }

public record JwtOptions
{
    public bool Enabled { get; set; } = true;
    public string Issuer { get; set; } = "ereceipt";
    public string Audience { get; set; } = "ereceipt-viewer";
    public int ExpMinutes { get; set; } = 10;
    public int SkewSeconds { get; set; } = 120;
    public string? Secret { get; set; }
    public string? ActiveKid { get; set; }
    public List<JwtKey>? Keys { get; set; }
}

public record StorageOptions
{
    public string Mode { get; set; } = "Sql";
    public string SqlProvider { get; set; } = "Sqlite";
    public ConnectionStrings ConnectionStrings { get; set; } = new();
}

public record ConnectionStrings
{
    public string Sqlite { get; set; } = "Data Source=app.db";
    public string SqlServer { get; set; } = "";
    public string Postgres { get; set; } = "";
}

public record RateLimitOptions
{
    public OtpRate Otp { get; set; } = new();
    public ShortenRate Shorten { get; set; } = new();
}

public record OtpRate
{
    public int PerIpPerMinute { get; set; } = 5;
    public int PerJtiPerMinute { get; set; } = 3;
}

public record ShortenRate
{
    public int PerIpPerMinute { get; set; } = 30;
    public int PerKeyPerHour { get; set; } = 120;
}

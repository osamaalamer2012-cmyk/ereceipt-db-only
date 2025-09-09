using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EReceiptAllInOne.Data;
using EReceiptAllInOne.Models;
using Microsoft.IdentityModel.Tokens;

namespace EReceiptAllInOne.Services;

public class JwtKeyRing
{
    private readonly Dictionary<string, SymmetricSecurityKey> _keys = new(StringComparer.Ordinal);
    private string _activeKid;

    public JwtKeyRing(JwtOptions opts)
    {
        if (opts.Keys is { Count: > 0 })
        {
            foreach (var k in opts.Keys)
            {
                if (string.IsNullOrWhiteSpace(k.Kid) || string.IsNullOrWhiteSpace(k.Secret) || k.Secret.Length < 32)
                    continue;
                _keys[k.Kid] = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(k.Secret));
            }
            _activeKid = string.IsNullOrWhiteSpace(opts.ActiveKid) ? opts.Keys[^1].Kid : opts.ActiveKid!;
        }
        else if (!string.IsNullOrWhiteSpace(opts.Secret))
        {
            _activeKid = "legacy";
            _keys[_activeKid] = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Secret));
        }
        else
        {
            throw new InvalidOperationException("JWT keys not configured");
        }
    }

    public string ActiveKid => _activeKid;

    public SigningCredentials GetActiveCredentials()
        => new SigningCredentials(_keys[_activeKid], SecurityAlgorithms.HmacSha256) { Kid = _activeKid };

    public IEnumerable<SecurityKey> AllKeys() => _keys.Values;

    public SecurityKey? GetKeyByKid(string? kid)
        => (kid != null && _keys.TryGetValue(kid, out var key)) ? key : null;

    public bool TrySetActiveKid(string kid)
    {
        if (!_keys.ContainsKey(kid)) return false;
        _activeKid = kid;
        return true;
    }
}

public class JwtService
{
    private readonly JwtOptions _opts;
    private readonly JwtKeyRing _ring;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtService(JwtOptions opts, JwtKeyRing ring)
    {
        _opts = opts;
        _ring = ring;
    }

    public string CreateToken(string jti, ReceiptEntity rec)
    {
        var now = DateTime.UtcNow;
        var exp = now.AddMinutes(_opts.ExpMinutes <= 0 ? 10 : _opts.ExpMinutes);
        var creds = _ring.GetActiveCredentials();
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, jti),
            new Claim("rid", rec.ReceiptId),
            new Claim("txn", rec.TxnId),
            new Claim("msisdn", rec.Msisdn),
            new Claim("amt", rec.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim("cur", rec.Currency),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            notBefore: now.AddSeconds(-30),
            expires: exp,
            signingCredentials: creds
        );
        return _handler.WriteToken(token);
    }

    public (bool ok, string? error, ClaimsPrincipal? principal) Validate(string token)
    {
        var parms = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _opts.Issuer,
            ValidateAudience = true,
            ValidAudience = _opts.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (t, st, kid, p) =>
            {
                if (kid != null)
                {
                    var key = _ring.GetKeyByKid(kid);
                    if (key != null) return new[] { key };
                }
                return _ring.AllKeys();
            },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(Math.Abs(_opts.SkewSeconds))
        };
        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, parms, out _);
            return (true, null, principal);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomerManager.Application.Common.Options;
using CustomerManager.Application.Interfaces;
using CustomerManager.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomerManager.Infrastructure.Authentication;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>JwtOptions is validated at startup (secret length etc., see
    /// Program.cs), so no re-validation is needed per token.</summary>
    public JwtTokenGenerator(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(User user)
    {
        // Short-lived by design — the refresh token is what keeps a session
        // alive, not a long-lived JWT that can't be revoked before expiry.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddMinutes(_options.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}

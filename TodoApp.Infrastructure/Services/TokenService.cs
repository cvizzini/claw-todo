using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TodoApp.Application.Interfaces;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Interfaces;

namespace TodoApp.Infrastructure.Services;

public class TokenService(IConfiguration config, IRefreshTokenRepository refreshTokens) : ITokenService
{
    public (string Token, DateTime Expires) GenerateAccessToken(
        string userId, string email, string? displayName,
        IList<string> roles, int? tenantId)
    {
        var jwtSection = config.GetSection("Jwt");
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var creds   = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(double.Parse(jwtSection["ExpiryHours"] ?? "8"));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   userId),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Email,              email),
            new(ClaimTypes.NameIdentifier,     userId),
            new("display_name",                displayName ?? email),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        if (tenantId.HasValue)
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer:             jwtSection["Issuer"],
            audience:           jwtSection["Audience"],
            claims:             claims,
            expires:            expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public async Task<string> CreateRefreshTokenAsync(string userId, CancellationToken ct = default)
    {
        // Purge old tokens first
        await refreshTokens.PurgeExpiredAsync(userId, ct);

        var tokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        await refreshTokens.AddAsync(new RefreshToken
        {
            Token     = tokenValue,
            UserId    = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        }, ct);
        await refreshTokens.SaveChangesAsync(ct);

        return tokenValue;
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VaultApp.Configuration;
using VaultApp.Models;

namespace VaultApp.Services;

public interface IJwtTokenService
{
    (string token, DateTime expiresAt) CreateToken(ApplicationUser user);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _opt;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _opt = options.Value;
    }

    public (string token, DateTime expiresAt) CreateToken(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(_opt.Key))
            throw new InvalidOperationException("Jwt:Key is not configured.");

        var expiresAt = DateTime.UtcNow.AddMinutes(_opt.ExpiresMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(ClaimTypes.NameIdentifier, user.Id)
        };

        var jwt = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return (token, expiresAt);
    }
}

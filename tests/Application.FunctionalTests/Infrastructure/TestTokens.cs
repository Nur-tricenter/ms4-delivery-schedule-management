using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace delivery_schedule_management.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Mints bearer tokens shaped like nur-tricenter-identity's: HMAC-SHA256, one <c>scope</c> claim per
/// permission, the platform's issuer/audience.
/// </summary>
public static class TestTokens
{
    public const string SecretKey = "functional-tests-signing-key-0123456789-abcdef";
    public const string Issuer = "nur-tricenter-identity";
    public const string Audience = "nur-tricenter-clients";

    public static string Create(params string[] scopes)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        claims.AddRange(scopes.Select(scope => new Claim("scope", scope)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            Issuer, Audience, claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

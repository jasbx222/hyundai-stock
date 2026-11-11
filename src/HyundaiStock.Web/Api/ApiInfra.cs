using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HyundaiStock.Web.Api;

public sealed class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "HyundaiStock";
    public string Audience { get; set; } = "HyundaiStock.Mobile";
    public int ExpiryHours { get; set; } = 24;
}

/// <summary>Uniform API envelope.</summary>
public class ApiResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public object? Data { get; init; }
    public IDictionary<string, string[]>? Errors { get; init; }
    public static ApiResponse Ok(object? data, string? message = null) => new() { Success = true, Data = data, Message = message };
    public static ApiResponse Fail(string message, IDictionary<string, string[]>? errors = null, object? data = null) => new() { Success = false, Message = message, Errors = errors, Data = data };
}

/// <summary>Standard paginated payload used in <c>data</c> for list endpoints.</summary>
public sealed record PageDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages)
{
    public static PageDto<T> From(PagedResult<T> p) => new(p.Items, p.Page, p.PageSize, p.TotalCount, p.TotalPages);
}

public sealed record TokenDto(string AccessToken, string TokenType, DateTime ExpiresAt, object User);

public sealed class TokenService(IOptions<JwtOptions> opt)
{
    public (string token, DateTime expires) Create(AppUser u)
    {
        var o = opt.Value;
        var expires = DateTime.UtcNow.AddHours(o.ExpiryHours <= 0 ? 24 : o.ExpiryHours);
        var claims = new[] { new Claim("sub", u.Id.ToString()), new Claim("name", u.Name), new Claim("typ", "app"), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")) };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var t = new JwtSecurityToken(o.Issuer, o.Audience, claims, DateTime.UtcNow, expires, creds);
        return (new JwtSecurityTokenHandler().WriteToken(t), expires);
    }
}

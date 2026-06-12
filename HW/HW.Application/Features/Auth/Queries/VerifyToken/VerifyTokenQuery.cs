using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HW.Application.CQRS;
using HW.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HW.Application.Features.Auth.Queries.VerifyToken;

public record VerifyTokenQuery(string Token) : IQuery<AppUser?>;

public class VerifyTokenQueryHandler : IQueryHandler<VerifyTokenQuery, AppUser?>
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _configuration;

    public VerifyTokenQueryHandler(UserManager<AppUser> userManager, IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    public async Task<AppUser?> Handle(VerifyTokenQuery request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return null;

        var key = _configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
            return null;

        try
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateIssuer = !string.IsNullOrWhiteSpace(_configuration["Jwt:Issuer"]),
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidateAudience = !string.IsNullOrWhiteSpace(_configuration["Jwt:Audience"]),
                ValidAudience = _configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(request.Token, parameters, out var validated);

            if (validated is not JwtSecurityToken)
                return null;

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return null;

            return await _userManager.FindByIdAsync(userId);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}

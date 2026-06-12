using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using static HW.Application.Features.Auth.Dtos.AuthDtos;

namespace HW.Application.Features.Auth.Queries.Login;

public record LoginQuery(string Username, string Password) : IQuery<LoginResponseDto>;

public class LoginQueryValidator : AbstractValidator<LoginQuery>
{
    public LoginQueryValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class LoginQueryHandler : IQueryHandler<LoginQuery, LoginResponseDto>
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _configuration;

    public LoginQueryHandler(UserManager<AppUser> userManager, IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    public async Task<LoginResponseDto> Handle(LoginQuery request, CancellationToken ct)
    {
        var user = await _userManager.FindByNameAsync(request.Username)
            ?? throw new UnauthorizedAccessException("Invalid username or password");

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
            throw new UnauthorizedAccessException("Invalid username or password");

        var accessToken = GenerateJwtToken(user, out var expiration);
        var refreshToken = GenerateRefreshToken();

        return new LoginResponseDto(user.UserName!, accessToken, refreshToken, expiration);
    }

    private string GenerateJwtToken(AppUser user, out DateTime expiration)
    {
        var key = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Missing configuration: Jwt:Key");

        var issuer = _configuration["Jwt:Issuer"] ?? string.Empty;
        var audience = _configuration["Jwt:Audience"] ?? string.Empty;
        var minutes = int.TryParse(_configuration["Jwt:AccessTokenExpirationMinutes"], out var m) ? m : 60;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        expiration = DateTime.UtcNow.AddMinutes(minutes);

        var token = new JwtSecurityToken(
            issuer: string.IsNullOrWhiteSpace(issuer) ? null : issuer,
            audience: string.IsNullOrWhiteSpace(audience) ? null : audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiration,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}

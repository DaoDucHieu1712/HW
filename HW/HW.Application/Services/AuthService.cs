using HW.Domain.Entities;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using static HW.Application.Dtos.AuthDtos;

namespace HW.Application.Services;

public interface IAuthService
{
    Task Register(RegisterRequestDto dto);
    Task<LoginResponseDto> Login(LoginRequestDto dto);
    Task<AppUser?> VerifyTokenAsync(string token);
}

public class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _configuration;

    public AuthService(
        UserManager<AppUser> userManager,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    public async Task Register(RegisterRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username))
            throw new ArgumentException("Username is required", nameof(dto.Username));
        if (string.IsNullOrWhiteSpace(dto.Password))
            throw new ArgumentException("Password is required", nameof(dto.Password));
        if (string.IsNullOrWhiteSpace(dto.Email))
            throw new ArgumentException("Email is required", nameof(dto.Email));

        var existingByName = await _userManager.FindByNameAsync(dto.Username);
        if (existingByName != null)
            throw new InvalidOperationException("Username is already taken");

        var existingByEmail = await _userManager.FindByEmailAsync(dto.Email);
        if (existingByEmail != null)
            throw new InvalidOperationException("Email is already taken");

        var user = new AppUser
        {
            FullName = dto.FullName,
            BirthDay = dto.BirthDay,
            UserName = dto.Username,
            Email = dto.Email,
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create user: {errors}");
        }
    }

    public async Task<LoginResponseDto> Login(LoginRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username))
            throw new ArgumentException("Username is required", nameof(dto.Username));
        if (string.IsNullOrWhiteSpace(dto.Password))
            throw new ArgumentException("Password is required", nameof(dto.Password));

        var user = await _userManager.FindByNameAsync(dto.Username);
        if (user == null)
            throw new UnauthorizedAccessException("Invalid username or password");

        var check = await  _userManager.CheckPasswordAsync(user, dto.Password);
        if (!check)
            throw new UnauthorizedAccessException("Invalid username or password");

        var accessToken = GenerateJwtToken(user, out DateTime expiration);
        var refreshToken = GenerateRefreshToken();

        return new LoginResponseDto(user.UserName, accessToken, refreshToken, expiration);
    }

    public async Task<AppUser?> VerifyTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var key = _configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(key))
                return null;

            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateIssuer = !string.IsNullOrWhiteSpace(_configuration["Jwt:Issuer"]),
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidateAudience = !string.IsNullOrWhiteSpace(_configuration["Jwt:Audience"]),
                ValidAudience = _configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken)
                return null;

            // Extract user ID from NameIdentifier claim
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return null;

            // Resolve user from UserManager
            var user = await _userManager.FindByIdAsync(userId);
            return user;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string GenerateJwtToken(AppUser user, out DateTime expiration)
    {
        var key = _configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Missing configuration: Jwt:Key");

        var issuer = _configuration["Jwt:Issuer"] ?? "";
        var audience = _configuration["Jwt:Audience"] ?? "";
        var accessMinutes = int.TryParse(_configuration["Jwt:AccessTokenExpirationMinutes"], out var m) ? m : 60;

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserName ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? string.Empty)
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));

        var keyBytes = Encoding.UTF8.GetBytes(key);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        expiration = DateTime.UtcNow.AddMinutes(accessMinutes);

        var token = new JwtSecurityToken(
            issuer: string.IsNullOrWhiteSpace(issuer) ? null : issuer,
            audience: string.IsNullOrWhiteSpace(audience) ? null : audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiration,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}

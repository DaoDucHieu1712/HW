namespace HW.Application.Features.Auth.Dtos;

public static class AuthDtos
{
    public record RegisterRequestDto(string FullName, DateTimeOffset BirthDay, string Username, string Password, string Email);
    public record LoginRequestDto(string Username, string Password);
    public record LoginResponseDto(string UserName, string AccessToken, string RefreshToken, DateTime Expiration);
    public record UserInfoDto(string Id, string Username, string FullName, string Email, string Status, string[] Permissions, string Orgs, string Metadata);
}

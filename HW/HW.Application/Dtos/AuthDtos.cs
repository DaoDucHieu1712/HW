namespace HW.Application.Dtos;

public static class AuthDtos
{
    public record RegisterRequestDto(string FullName, DateTimeOffset BirthDay, string Username, string Password, string Email);
    public record LoginRequestDto(string Username, string Password);
    public record LoginResponseDto(string UserName ,string AccessToken, string RefreshToken,DateTime Expiration);
}

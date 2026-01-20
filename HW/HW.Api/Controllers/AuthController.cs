using HW.Application.Services;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Dtos.AuthDtos;

namespace HW.Api.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequestDto request)
    {
        await _authService.Register(request);
        return NoContent();
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequestDto request)
    {
        var result = await _authService.Login(request);
        return Ok(result);
    }
}
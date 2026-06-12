using HW.Api.Models;
using HW.Application.Features.Auth.Commands.Register;
using HW.Application.Features.Auth.Queries.Login;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Features.Auth.Dtos.AuthDtos;

namespace HW.Api.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender) => _sender = sender;

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequestDto dto)
    {
        await _sender.Send(new RegisterCommand(dto.FullName, dto.BirthDay, dto.Username, dto.Password, dto.Email));
        return NoContent();
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequestDto dto)
    {
        var result = await _sender.Send(new LoginQuery(dto.Username, dto.Password));
        return Ok(ApiResponseFactory.Success(result));
    }
}

using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using HW.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace HW.Application.Features.Auth.Commands.Register;

public record RegisterCommand(string FullName, DateTimeOffset BirthDay, string Username, string Password, string Email) : ICommand;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty();
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(3);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public class RegisterCommandHandler : ICommandHandler<RegisterCommand>
{
    private readonly UserManager<AppUser> _userManager;

    public RegisterCommandHandler(UserManager<AppUser> userManager)
        => _userManager = userManager;

    public async Task<Unit> Handle(RegisterCommand request, CancellationToken ct)
    {
        var email = Email.Create(request.Email);

        if (await _userManager.FindByNameAsync(request.Username) != null)
            throw new BadRequestException("Username is already taken");

        if (await _userManager.FindByEmailAsync(email.Value) != null)
            throw new BadRequestException("Email is already taken");

        var user = new AppUser
        {
            FullName = request.FullName,
            BirthDay = request.BirthDay,
            UserName = request.Username,
            Email = email.Value,
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new BadRequestException(errors);
        }

        return Unit.Value;
    }
}

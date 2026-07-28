using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using MediatR;

namespace HW.Application.Features.UserFitnessProfiles.Commands.DeleteUserFitnessProfile;

public record DeleteUserFitnessProfileCommand(string Id) : ICommand;

public class DeleteUserFitnessProfileCommandValidator : AbstractValidator<DeleteUserFitnessProfileCommand>
{
    public DeleteUserFitnessProfileCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class DeleteUserFitnessProfileCommandHandler : ICommandHandler<DeleteUserFitnessProfileCommand>
{
    private readonly IRepository<UserFitnessProfile> _repository;

    public DeleteUserFitnessProfileCommandHandler(IRepository<UserFitnessProfile> repository)
        => _repository = repository;

    public async Task<Unit> Handle(DeleteUserFitnessProfileCommand request, CancellationToken ct)
    {
        var profile = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new UserFitnessProfileNotFoundException(request.Id);

        profile.SoftDelete();
        _repository.Update(profile);
        return await Task.FromResult(Unit.Value);
    }
}

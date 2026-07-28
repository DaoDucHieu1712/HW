using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Enums;
using HW.Domain.ValueObjects;
using MediatR;

namespace HW.Application.Features.UserFitnessProfiles.Commands.CreateUserFitnessProfile;

public record CreateUserFitnessProfileCommand(
    int Age,
    Gender Gender,
    decimal HeightCm,
    decimal WeightKg,
    ActivityLevel ActivityLevel,
    FitnessLevel FitnessLevel,
    FitnessGoal Goal,
    int AvailableDaysPerWeek,
    List<Equipment> AvailableEquipment,
    DietaryPreference DietaryPreference,
    int MealsPerDay,
    List<string> Allergies,
    List<string> InjuriesOrLimitations) : ICommand;

public class CreateUserFitnessProfileCommandValidator : AbstractValidator<CreateUserFitnessProfileCommand>
{
    public CreateUserFitnessProfileCommandValidator()
    {
        RuleFor(x => x.Age).InclusiveBetween(1, 120);
        RuleFor(x => x.HeightCm).InclusiveBetween(50, 300);
        RuleFor(x => x.WeightKg).InclusiveBetween(10, 500);
        RuleFor(x => x.AvailableDaysPerWeek).InclusiveBetween(3, 6);
        RuleFor(x => x.MealsPerDay).InclusiveBetween(3, 5);
        RuleFor(x => x.AvailableEquipment).NotEmpty();
    }
}

public class CreateUserFitnessProfileCommandHandler : ICommandHandler<CreateUserFitnessProfileCommand>
{
    private readonly IRepository<UserFitnessProfile> _repository;

    public CreateUserFitnessProfileCommandHandler(IRepository<UserFitnessProfile> repository)
        => _repository = repository;

    public async Task<Unit> Handle(CreateUserFitnessProfileCommand request, CancellationToken ct)
    {
        var personalInfo = PersonalInfo.Create(request.Age, request.Gender, request.HeightCm, request.WeightKg);

        var profile = new UserFitnessProfile(
            personalInfo,
            request.ActivityLevel,
            request.FitnessLevel,
            request.Goal,
            request.AvailableDaysPerWeek,
            request.AvailableEquipment,
            request.DietaryPreference,
            request.MealsPerDay,
            request.Allergies,
            request.InjuriesOrLimitations);

        _repository.Add(profile);
        return await Task.FromResult(Unit.Value);
    }
}

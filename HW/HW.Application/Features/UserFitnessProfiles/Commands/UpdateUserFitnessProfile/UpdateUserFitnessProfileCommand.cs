using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Enums;
using HW.Domain.Exceptions;
using HW.Domain.ValueObjects;
using MediatR;

namespace HW.Application.Features.UserFitnessProfiles.Commands.UpdateUserFitnessProfile;

public record UpdateUserFitnessProfileCommand(
    string Id,
    int? Age,
    Gender? Gender,
    decimal? HeightCm,
    decimal? WeightKg,
    ActivityLevel? ActivityLevel,
    FitnessLevel? FitnessLevel,
    FitnessGoal? Goal,
    int? AvailableDaysPerWeek,
    List<Equipment>? AvailableEquipment,
    DietaryPreference? DietaryPreference,
    int? MealsPerDay,
    List<string>? Allergies,
    List<string>? InjuriesOrLimitations) : ICommand;

public class UpdateUserFitnessProfileCommandValidator : AbstractValidator<UpdateUserFitnessProfileCommand>
{
    public UpdateUserFitnessProfileCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Age).InclusiveBetween(1, 120).When(x => x.Age.HasValue);
        RuleFor(x => x.HeightCm).InclusiveBetween(50, 300).When(x => x.HeightCm.HasValue);
        RuleFor(x => x.WeightKg).InclusiveBetween(10, 500).When(x => x.WeightKg.HasValue);
        RuleFor(x => x.AvailableDaysPerWeek).InclusiveBetween(3, 6).When(x => x.AvailableDaysPerWeek.HasValue);
        RuleFor(x => x.MealsPerDay).InclusiveBetween(3, 5).When(x => x.MealsPerDay.HasValue);
    }
}

public class UpdateUserFitnessProfileCommandHandler : ICommandHandler<UpdateUserFitnessProfileCommand>
{
    private readonly IRepository<UserFitnessProfile> _repository;

    public UpdateUserFitnessProfileCommandHandler(IRepository<UserFitnessProfile> repository)
        => _repository = repository;

    public async Task<Unit> Handle(UpdateUserFitnessProfileCommand request, CancellationToken ct)
    {
        var profile = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new UserFitnessProfileNotFoundException(request.Id);

        PersonalInfo? personalInfo = null;
        if (request.Age.HasValue || request.Gender.HasValue || request.HeightCm.HasValue || request.WeightKg.HasValue)
        {
            personalInfo = PersonalInfo.Create(
                request.Age ?? profile.PersonalInfo.Age,
                request.Gender ?? profile.PersonalInfo.Gender,
                request.HeightCm ?? profile.PersonalInfo.HeightCm,
                request.WeightKg ?? profile.PersonalInfo.WeightKg);
        }

        profile.Update(
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

        _repository.Update(profile);
        return Unit.Value;
    }
}

using FluentValidation;
using HW.Application.CQRS;
using HW.Domain.Entities;
using HW.Domain.Enums;
using HW.Domain.ValueObjects;
using static HW.Application.Features.UserFitnessProfiles.Dtos.WorkoutPlanDtos;

namespace HW.Application.Features.UserFitnessProfiles.Queries.GenerateWorkoutPlan;

public record GenerateWorkoutPlanQuery(
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
    List<string> InjuriesOrLimitations
) : IQuery<GeneratedPlanResponseDto>;

public class GenerateWorkoutPlanQueryValidator : AbstractValidator<GenerateWorkoutPlanQuery>
{
    public GenerateWorkoutPlanQueryValidator()
    {
        RuleFor(x => x.Age).InclusiveBetween(1, 120);
        RuleFor(x => x.HeightCm).InclusiveBetween(50, 300);
        RuleFor(x => x.WeightKg).InclusiveBetween(10, 500);
        RuleFor(x => x.AvailableDaysPerWeek).InclusiveBetween(3, 6);
        RuleFor(x => x.MealsPerDay).InclusiveBetween(3, 5);
        RuleFor(x => x.AvailableEquipment).NotEmpty();
    }
}

public class GenerateWorkoutPlanQueryHandler : IQueryHandler<GenerateWorkoutPlanQuery, GeneratedPlanResponseDto>
{
    public Task<GeneratedPlanResponseDto> Handle(GenerateWorkoutPlanQuery request, CancellationToken ct)
    {
        var personalInfo = PersonalInfo.Create(
            request.Age, request.Gender, request.HeightCm, request.WeightKg);

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

        return Task.FromResult(WorkoutPlanRecipe.Generate(profile));
    }
}

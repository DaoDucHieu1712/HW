using HW.Application.CQRS;
using HW.Domain.Entities;
using HW.Domain.Enums;
using HW.Domain.ValueObjects;
using static HW.Application.Features.UserFitnessProfiles.Dtos.WorkoutPlanDtos;

namespace HW.Application.Features.UserFitnessProfiles.Queries.GenerateRandomWorkoutPlan;

public record GenerateRandomWorkoutPlanQuery : IQuery<RandomWorkoutPlanResponseDto>;

public class GenerateRandomWorkoutPlanQueryHandler
    : IQueryHandler<GenerateRandomWorkoutPlanQuery, RandomWorkoutPlanResponseDto>
{
    private static readonly Random Rng = new();

    public Task<RandomWorkoutPlanResponseDto> Handle(
        GenerateRandomWorkoutPlanQuery request, CancellationToken ct)
    {
        var input = BuildRandomInput();

        var personalInfo = PersonalInfo.Create(
            input.Age,
            Enum.Parse<Gender>(input.Gender),
            input.HeightCm,
            input.WeightKg);

        var profile = new UserFitnessProfile(
            personalInfo,
            Enum.Parse<ActivityLevel>(input.ActivityLevel),
            Enum.Parse<FitnessLevel>(input.FitnessLevel),
            Enum.Parse<FitnessGoal>(input.Goal),
            input.AvailableDaysPerWeek,
            input.AvailableEquipment.Select(Enum.Parse<Equipment>).ToList(),
            Enum.Parse<DietaryPreference>(input.DietaryPreference),
            input.MealsPerDay,
            input.Allergies ?? [],
            input.InjuriesOrLimitations ?? []);

        var plan = WorkoutPlanRecipe.Generate(profile);

        return Task.FromResult(new RandomWorkoutPlanResponseDto(input, plan));
    }

    private static GenerateWorkoutPlanRequestDto BuildRandomInput()
    {
        var gender = PickRandom<Gender>();
        var (height, weight) = gender == Gender.Female
            ? (RngDecimal(155, 175), RngDecimal(48, 80))
            : (RngDecimal(165, 195), RngDecimal(60, 100));

        var allEquipment = Enum.GetNames<Equipment>();
        var equipmentCount = Rng.Next(1, allEquipment.Length + 1);
        var equipment = allEquipment.OrderBy(_ => Rng.Next()).Take(equipmentCount).ToList();

        var allergies = new[] { "Peanuts", "Gluten", "Dairy", "Shellfish", "Eggs", "Soy" };
        var selectedAllergies = allergies.Where(_ => Rng.Next(5) == 0).ToList();

        var injuries = new[] { "Lower back pain", "Knee issues", "Shoulder impingement", "Wrist strain" };
        var selectedInjuries = injuries.Where(_ => Rng.Next(6) == 0).ToList();

        return new GenerateWorkoutPlanRequestDto(
            Age:                    Rng.Next(18, 61),
            Gender:                 gender.ToString(),
            HeightCm:               height,
            WeightKg:               weight,
            ActivityLevel:          PickRandom<ActivityLevel>().ToString(),
            FitnessLevel:           PickRandom<FitnessLevel>().ToString(),
            Goal:                   PickRandom<FitnessGoal>().ToString(),
            AvailableDaysPerWeek:   Rng.Next(3, 7),
            AvailableEquipment:     equipment,
            DietaryPreference:      PickRandom<DietaryPreference>().ToString(),
            MealsPerDay:            Rng.Next(3, 6),
            Allergies:              selectedAllergies,
            InjuriesOrLimitations:  selectedInjuries);
    }

    private static T PickRandom<T>() where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return values[Rng.Next(values.Length)];
    }

    private static decimal RngDecimal(double min, double max) =>
        Math.Round((decimal)(Rng.NextDouble() * (max - min) + min), 1);
}

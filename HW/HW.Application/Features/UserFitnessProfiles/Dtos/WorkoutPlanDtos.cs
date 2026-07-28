namespace HW.Application.Features.UserFitnessProfiles.Dtos;

public static class WorkoutPlanDtos
{
    public record GenerateWorkoutPlanRequestDto(
        int Age,
        string Gender,
        decimal HeightCm,
        decimal WeightKg,
        string ActivityLevel,
        string FitnessLevel,
        string Goal,
        int AvailableDaysPerWeek,
        List<string> AvailableEquipment,
        string DietaryPreference,
        int MealsPerDay,
        List<string>? Allergies,
        List<string>? InjuriesOrLimitations);

    public record GeneratedPlanResponseDto(
        List<DayWorkoutDto> WeeklyWorkoutPlan,
        List<DayMealPlanDto> WeeklyMealPlan,
        GroceryListDto GroceryList);

    public record DayWorkoutDto(
        string Day,
        List<ExerciseDto> Exercises);

    public record ExerciseDto(
        string Name,
        int Sets,
        string Reps,
        string Weight,
        int RestSeconds,
        string Notes);

    public record DayMealPlanDto(
        string Day,
        int TotalCalories,
        MealDto Breakfast,
        MealDto Lunch,
        MealDto Dinner);

    public record MealDto(
        string Name,
        int Calories,
        int Protein,
        int Carbs,
        int Fat);

    public record GroceryListDto(
        List<string> Produce,
        List<string> Protein,
        List<string> Grains,
        List<string> Dairy,
        List<string> Other);

    public record RandomWorkoutPlanResponseDto(
        GenerateWorkoutPlanRequestDto Input,
        GeneratedPlanResponseDto Plan);
}

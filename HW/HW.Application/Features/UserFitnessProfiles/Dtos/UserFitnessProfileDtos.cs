namespace HW.Application.Features.UserFitnessProfiles.Dtos;

public static class UserFitnessProfileDtos
{
    public record UserFitnessProfileResponseDto(
        string Id,
        int Age,
        string Gender,
        decimal HeightCm,
        decimal WeightKg,
        decimal Bmi,
        decimal Bmr,
        string ActivityLevel,
        string FitnessLevel,
        string Goal,
        int AvailableDaysPerWeek,
        List<string> AvailableEquipment,
        string DietaryPreference,
        int MealsPerDay,
        List<string> Allergies,
        List<string> InjuriesOrLimitations,
        DateTimeOffset? CreatedAt,
        string? CreatedBy,
        DateTimeOffset? UpdatedAt,
        string? UpdatedBy);

    public record GetUserFitnessProfilesRequestDto(string? Search, int PageIndex, int PageSize);

    public record CreateUserFitnessProfileRequestDto(
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

    public record UpdateUserFitnessProfileRequestDto(
        int? Age,
        string? Gender,
        decimal? HeightCm,
        decimal? WeightKg,
        string? ActivityLevel,
        string? FitnessLevel,
        string? Goal,
        int? AvailableDaysPerWeek,
        List<string>? AvailableEquipment,
        string? DietaryPreference,
        int? MealsPerDay,
        List<string>? Allergies,
        List<string>? InjuriesOrLimitations);
}

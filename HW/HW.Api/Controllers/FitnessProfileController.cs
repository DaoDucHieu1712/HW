using HW.Api.Models;
using HW.Application.Features.UserFitnessProfiles.Commands.CreateUserFitnessProfile;
using HW.Application.Features.UserFitnessProfiles.Commands.DeleteUserFitnessProfile;
using HW.Application.Features.UserFitnessProfiles.Commands.UpdateUserFitnessProfile;
using HW.Application.Features.UserFitnessProfiles.Queries.GenerateRandomWorkoutPlan;
using HW.Application.Features.UserFitnessProfiles.Queries.GenerateWorkoutPlan;
using HW.Application.Features.UserFitnessProfiles.Queries.GetUserFitnessProfileById;
using HW.Application.Features.UserFitnessProfiles.Queries.GetUserFitnessProfiles;
using HW.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using static HW.Application.Features.UserFitnessProfiles.Dtos.UserFitnessProfileDtos;
using static HW.Application.Features.UserFitnessProfiles.Dtos.WorkoutPlanDtos;

namespace HW.Api.Controllers;

[Route("api/fitness-profile")]
[ApiController]
public class FitnessProfileController : ControllerBase
{
    private readonly ISender _sender;

    public FitnessProfileController(ISender sender) => _sender = sender;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] GetUserFitnessProfilesRequestDto dto)
    {
        var rs = await _sender.Send(new GetUserFitnessProfilesQuery(dto.Search, dto.PageIndex, dto.PageSize));
        return Ok(ApiResponseFactory.Success(rs));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var rs = await _sender.Send(new GetUserFitnessProfileByIdQuery(id));
        return Ok(ApiResponseFactory.Success(rs));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserFitnessProfileRequestDto dto)
    {
        await _sender.Send(new CreateUserFitnessProfileCommand(
            dto.Age,
            ParseEnum<Gender>(dto.Gender),
            dto.HeightCm,
            dto.WeightKg,
            ParseEnum<ActivityLevel>(dto.ActivityLevel),
            ParseEnum<FitnessLevel>(dto.FitnessLevel),
            ParseEnum<FitnessGoal>(dto.Goal),
            dto.AvailableDaysPerWeek,
            dto.AvailableEquipment.Select(ParseEnum<Equipment>).ToList(),
            ParseEnum<DietaryPreference>(dto.DietaryPreference),
            dto.MealsPerDay,
            dto.Allergies ?? [],
            dto.InjuriesOrLimitations ?? []));

        return NoContent();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserFitnessProfileRequestDto dto)
    {
        await _sender.Send(new UpdateUserFitnessProfileCommand(
            id,
            dto.Age,
            dto.Gender is not null ? ParseEnum<Gender>(dto.Gender) : null,
            dto.HeightCm,
            dto.WeightKg,
            dto.ActivityLevel is not null ? ParseEnum<ActivityLevel>(dto.ActivityLevel) : null,
            dto.FitnessLevel is not null ? ParseEnum<FitnessLevel>(dto.FitnessLevel) : null,
            dto.Goal is not null ? ParseEnum<FitnessGoal>(dto.Goal) : null,
            dto.AvailableDaysPerWeek,
            dto.AvailableEquipment?.Select(ParseEnum<Equipment>).ToList(),
            dto.DietaryPreference is not null ? ParseEnum<DietaryPreference>(dto.DietaryPreference) : null,
            dto.MealsPerDay,
            dto.Allergies,
            dto.InjuriesOrLimitations));

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await _sender.Send(new DeleteUserFitnessProfileCommand(id));
        return NoContent();
    }

    [HttpGet("generate-plan/random")]
    public async Task<IActionResult> GenerateRandomPlan()
    {
        var rs = await _sender.Send(new GenerateRandomWorkoutPlanQuery());
        return Ok(ApiResponseFactory.Success(rs));
    }

    [HttpPost("generate-plan")]
    public async Task<IActionResult> GeneratePlan([FromBody] GenerateWorkoutPlanRequestDto dto)
    {
        var rs = await _sender.Send(new GenerateWorkoutPlanQuery(
            dto.Age,
            ParseEnum<Gender>(dto.Gender),
            dto.HeightCm,
            dto.WeightKg,
            ParseEnum<ActivityLevel>(dto.ActivityLevel),
            ParseEnum<FitnessLevel>(dto.FitnessLevel),
            ParseEnum<FitnessGoal>(dto.Goal),
            dto.AvailableDaysPerWeek,
            dto.AvailableEquipment.Select(ParseEnum<Equipment>).ToList(),
            ParseEnum<DietaryPreference>(dto.DietaryPreference),
            dto.MealsPerDay,
            dto.Allergies ?? [],
            dto.InjuriesOrLimitations ?? []));

        return Ok(ApiResponseFactory.Success(rs));
    }

    // Accepts exact name ("ModeratelyActive"), case-insensitive ("moderatelyactive"),
    // or a prefix/partial match ("moderate" → ModeratelyActive).
    private static T ParseEnum<T>(string value) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var exact))
            return exact;

        var match = Enum.GetNames(typeof(T))
            .FirstOrDefault(n => n.StartsWith(value, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return Enum.Parse<T>(match);

        var valid = string.Join(", ", Enum.GetNames(typeof(T)));
        throw new ArgumentException($"'{value}' is not a valid {typeof(T).Name}. Valid values: {valid}.");
    }
}

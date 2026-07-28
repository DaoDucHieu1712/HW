using HW.Application.CQRS;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Exceptions;
using HW.Domain.ValueObjects;
using static HW.Application.Features.UserFitnessProfiles.Dtos.UserFitnessProfileDtos;

namespace HW.Application.Features.UserFitnessProfiles.Queries.GetUserFitnessProfileById;

public record GetUserFitnessProfileByIdQuery(string Id) : IQuery<UserFitnessProfileResponseDto>;

public class GetUserFitnessProfileByIdQueryHandler
    : IQueryHandler<GetUserFitnessProfileByIdQuery, UserFitnessProfileResponseDto>
{
    private readonly IRepository<UserFitnessProfile> _repository;

    public GetUserFitnessProfileByIdQueryHandler(IRepository<UserFitnessProfile> repository)
        => _repository = repository;

    public async Task<UserFitnessProfileResponseDto> Handle(
        GetUserFitnessProfileByIdQuery request, CancellationToken ct)
    {
        var profile = await _repository.FindByIdAsync(request.Id, ct)
            ?? throw new UserFitnessProfileNotFoundException(request.Id);

        var metrics = BodyMetrics.Compute(profile.PersonalInfo);

        return new UserFitnessProfileResponseDto(
            profile.Id,
            profile.PersonalInfo.Age,
            profile.PersonalInfo.Gender.ToString(),
            profile.PersonalInfo.HeightCm,
            profile.PersonalInfo.WeightKg,
            metrics.Bmi,
            metrics.Bmr,
            profile.ActivityLevel.ToString(),
            profile.FitnessLevel.ToString(),
            profile.Goal.ToString(),
            profile.AvailableDaysPerWeek,
            profile.AvailableEquipment.Select(e => e.ToString()).ToList(),
            profile.DietaryPreference.ToString(),
            profile.MealsPerDay,
            profile.Allergies,
            profile.InjuriesOrLimitations,
            profile.CreatedAt,
            profile.CreatedBy,
            profile.UpdatedAt,
            profile.UpdatedBy);
    }
}

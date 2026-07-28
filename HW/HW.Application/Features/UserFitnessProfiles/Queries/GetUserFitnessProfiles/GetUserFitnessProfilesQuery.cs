using HW.Application.CQRS;
using HW.Domain.Abstractions.Entities;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Domain.Enums;
using HW.Domain.ValueObjects;
using Mapster;
using static HW.Application.Features.UserFitnessProfiles.Dtos.UserFitnessProfileDtos;

namespace HW.Application.Features.UserFitnessProfiles.Queries.GetUserFitnessProfiles;

public record GetUserFitnessProfilesQuery(string? Search, int PageIndex, int PageSize)
    : IQuery<PagedResult<UserFitnessProfileResponseDto>>;

public class GetUserFitnessProfilesQueryHandler
    : IQueryHandler<GetUserFitnessProfilesQuery, PagedResult<UserFitnessProfileResponseDto>>
{
    private readonly IRepository<UserFitnessProfile> _repository;

    public GetUserFitnessProfilesQueryHandler(IRepository<UserFitnessProfile> repository)
        => _repository = repository;

    public async Task<PagedResult<UserFitnessProfileResponseDto>> Handle(
        GetUserFitnessProfilesQuery request, CancellationToken ct)
    {
        var source = _repository.FindAll();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            source = source.Where(p =>
                p.ActivityLevel.ToString().ToLower().Contains(search) ||
                p.FitnessLevel.ToString().ToLower().Contains(search) ||
                p.Goal.ToString().ToLower().Contains(search));
        }

        var paged = await PagedResult<UserFitnessProfile>.CreateAsync(source, request.PageIndex, request.PageSize);

        return new PagedResult<UserFitnessProfileResponseDto>(
            paged.Items.Select(MapToDto).ToList(),
            paged.PageIndex,
            paged.PageSize,
            paged.TotalCount);
    }

    private static UserFitnessProfileResponseDto MapToDto(UserFitnessProfile p)
    {
        var metrics = BodyMetrics.Compute(p.PersonalInfo);
        return new UserFitnessProfileResponseDto(
            p.Id,
            p.PersonalInfo.Age,
            p.PersonalInfo.Gender.ToString(),
            p.PersonalInfo.HeightCm,
            p.PersonalInfo.WeightKg,
            metrics.Bmi,
            metrics.Bmr,
            p.ActivityLevel.ToString(),
            p.FitnessLevel.ToString(),
            p.Goal.ToString(),
            p.AvailableDaysPerWeek,
            p.AvailableEquipment.Select(e => e.ToString()).ToList(),
            p.DietaryPreference.ToString(),
            p.MealsPerDay,
            p.Allergies,
            p.InjuriesOrLimitations,
            p.CreatedAt,
            p.CreatedBy,
            p.UpdatedAt,
            p.UpdatedBy);
    }
}

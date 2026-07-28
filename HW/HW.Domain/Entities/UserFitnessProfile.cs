using HW.Domain.Abstractions.Entities;
using HW.Domain.Enums;
using HW.Domain.Events.UserFitnessProfiles;
using HW.Domain.ValueObjects;

namespace HW.Domain.Entities;

public class UserFitnessProfile : AggregateRoot, IAuditableEntity, ISoftDeleteEntity
{
    protected UserFitnessProfile() { }

    public UserFitnessProfile(
        PersonalInfo personalInfo,
        ActivityLevel activityLevel,
        FitnessLevel fitnessLevel,
        FitnessGoal goal,
        int availableDaysPerWeek,
        List<Equipment> availableEquipment,
        DietaryPreference dietaryPreference,
        int mealsPerDay,
        List<string> allergies,
        List<string> injuriesOrLimitations)
    {
        PersonalInfo = personalInfo;
        ActivityLevel = activityLevel;
        FitnessLevel = fitnessLevel;
        Goal = goal;
        AvailableDaysPerWeek = availableDaysPerWeek;
        AvailableEquipment = availableEquipment;
        DietaryPreference = dietaryPreference;
        MealsPerDay = mealsPerDay;
        Allergies = allergies;
        InjuriesOrLimitations = injuriesOrLimitations;

        RaiseDomainEvent(new UserFitnessProfileCreatedDomainEvent(Id));
    }

    public PersonalInfo PersonalInfo { get; private set; } = null!;
    public ActivityLevel ActivityLevel { get; private set; }
    public FitnessLevel FitnessLevel { get; private set; }
    public FitnessGoal Goal { get; private set; }
    public int AvailableDaysPerWeek { get; private set; }
    public virtual List<Equipment> AvailableEquipment { get; private set; } = [];
    public DietaryPreference DietaryPreference { get; private set; }
    public int MealsPerDay { get; private set; }
    public virtual List<string> Allergies { get; private set; } = [];
    public virtual List<string> InjuriesOrLimitations { get; private set; } = [];

    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool? IsDelete { get; set; }

    public void Update(
        PersonalInfo? personalInfo,
        ActivityLevel? activityLevel,
        FitnessLevel? fitnessLevel,
        FitnessGoal? goal,
        int? availableDaysPerWeek,
        List<Equipment>? availableEquipment,
        DietaryPreference? dietaryPreference,
        int? mealsPerDay,
        List<string>? allergies,
        List<string>? injuriesOrLimitations)
    {
        if (personalInfo is not null) PersonalInfo = personalInfo;
        if (activityLevel is not null) ActivityLevel = activityLevel.Value;
        if (fitnessLevel is not null) FitnessLevel = fitnessLevel.Value;
        if (goal is not null) Goal = goal.Value;
        if (availableDaysPerWeek is not null) AvailableDaysPerWeek = availableDaysPerWeek.Value;
        if (availableEquipment is not null) AvailableEquipment = availableEquipment;
        if (dietaryPreference is not null) DietaryPreference = dietaryPreference.Value;
        if (mealsPerDay is not null) MealsPerDay = mealsPerDay.Value;
        if (allergies is not null) Allergies = allergies;
        if (injuriesOrLimitations is not null) InjuriesOrLimitations = injuriesOrLimitations;

        RaiseDomainEvent(new UserFitnessProfileUpdatedDomainEvent(Id));
    }

    public void SoftDelete()
    {
        IsDelete = true;
        RaiseDomainEvent(new UserFitnessProfileDeletedDomainEvent(Id));
    }
}

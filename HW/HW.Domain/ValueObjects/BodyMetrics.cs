using HW.Domain.Enums;

namespace HW.Domain.ValueObjects;

public sealed class BodyMetrics
{
    public decimal Bmi { get; }
    public decimal Bmr { get; }

    private BodyMetrics(decimal bmi, decimal bmr)
    {
        Bmi = bmi;
        Bmr = bmr;
    }

    public static BodyMetrics Compute(PersonalInfo info)
    {
        var heightM = info.HeightCm / 100m;
        var bmi = Math.Round(info.WeightKg / (heightM * heightM), 2);

        // Mifflin-St Jeor formula
        var bmr = info.Gender == Gender.Male
            ? Math.Round(10m * info.WeightKg + 6.25m * info.HeightCm - 5m * info.Age + 5m, 2)
            : Math.Round(10m * info.WeightKg + 6.25m * info.HeightCm - 5m * info.Age - 161m, 2);

        return new BodyMetrics(bmi, bmr);
    }
}

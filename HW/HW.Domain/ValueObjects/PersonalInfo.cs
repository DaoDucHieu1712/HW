using HW.Domain.Abstractions.ValueObjects;
using HW.Domain.Enums;
using HW.Domain.Exceptions;

namespace HW.Domain.ValueObjects;

public class PersonalInfo : ValueObject<PersonalInfo>
{
    protected PersonalInfo() { }

    private PersonalInfo(int age, Gender gender, decimal heightCm, decimal weightKg)
    {
        Age = age;
        Gender = gender;
        HeightCm = heightCm;
        WeightKg = weightKg;
    }

    public int Age { get; private set; }
    public Gender Gender { get; private set; }
    public decimal HeightCm { get; private set; }
    public decimal WeightKg { get; private set; }

    public static PersonalInfo Create(int age, Gender gender, decimal heightCm, decimal weightKg)
    {
        if (age < 1 || age > 120)
            throw new BadRequestException("Age must be between 1 and 120.");
        if (heightCm < 50 || heightCm > 300)
            throw new BadRequestException("Height must be between 50 and 300 cm.");
        if (weightKg < 10 || weightKg > 500)
            throw new BadRequestException("Weight must be between 10 and 500 kg.");

        return new PersonalInfo(age, gender, heightCm, weightKg);
    }

    public static PersonalInfo FromPersistence(int age, Gender gender, decimal heightCm, decimal weightKg)
        => new(age, gender, heightCm, weightKg);

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Age;
        yield return Gender;
        yield return HeightCm;
        yield return WeightKg;
    }
}

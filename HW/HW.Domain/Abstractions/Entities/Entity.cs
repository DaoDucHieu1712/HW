namespace HW.Domain.Abstractions.Entities;

public abstract class Entity
{
    protected Entity()
    {
        Id = Guid.NewGuid().ToString();
    }

    public string Id { get; set; }
}

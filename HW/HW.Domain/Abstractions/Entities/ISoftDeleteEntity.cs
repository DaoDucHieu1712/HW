namespace HW.Domain.Abstractions.Entities;

public interface ISoftDeleteEntity
{
    public bool? IsDelete { get; set; }
}

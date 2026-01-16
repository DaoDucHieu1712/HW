namespace HW.Domain.Abstractions.Entities;

public interface IAuditableEntity
{
    DateTimeOffset? CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}

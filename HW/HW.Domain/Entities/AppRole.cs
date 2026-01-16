using Microsoft.AspNetCore.Identity;

namespace HW.Domain.Entities;

public class AppRole : IdentityRole
{
    public string Description { get; set; }
}

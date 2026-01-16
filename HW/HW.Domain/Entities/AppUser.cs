using Microsoft.AspNetCore.Identity;

namespace HW.Domain.Entities
{
    public class AppUser : IdentityUser
    {
        public string FullName { get; set; }
        public DateTimeOffset BirthDay { get; set; }

    }
}

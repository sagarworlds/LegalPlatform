using Microsoft.AspNetCore.Identity;

namespace IdentityService.Domain;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() : base() { }
    public ApplicationRole(string roleName) : base(roleName) { }
}

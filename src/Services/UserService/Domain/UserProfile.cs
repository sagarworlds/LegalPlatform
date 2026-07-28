using SharedKernel.Domain;

namespace UserService.Domain;

public class UserProfile : AggregateRoot<Guid>
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public Guid? OrganizationId { get; set; }

    public UserProfile(Guid id, string email, string firstName, string lastName, Guid? organizationId) : base(id)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        OrganizationId = organizationId;
    }

#pragma warning disable CS8618
    protected UserProfile() { }
#pragma warning restore CS8618
}

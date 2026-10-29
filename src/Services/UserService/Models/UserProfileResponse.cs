using UserService.Domain;

namespace UserService.Models;

/// <summary>Response shape for the User API; keeps the aggregate's internals (such as its domain-event list) out of the JSON.</summary>
public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    string? PhoneNumber,
    Guid? OrganizationId)
{
    public static UserProfileResponse From(UserProfile profile) =>
        new(profile.Id, profile.Email, profile.FirstName, profile.LastName, profile.AvatarUrl, profile.PhoneNumber, profile.OrganizationId);
}

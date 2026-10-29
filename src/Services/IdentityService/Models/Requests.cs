namespace IdentityService.Models;

public record RegisterRequest(string Email, string Password, string? FirstName = null, string? LastName = null, Guid? OrganizationId = null);
public record LoginRequest(string Email, string Password);
public record RefreshTokenRequest(string AccessToken, string RefreshToken);
public record AssignRolesRequest(IReadOnlyList<string> Roles);
public record AssignOrganizationRequest(Guid OrganizationId);

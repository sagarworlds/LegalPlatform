namespace IdentityService.Models;

public record RegisterRequest(string Email, string Password, string FirstName, string LastName, Guid? OrganizationId);
public record LoginRequest(string Email, string Password);
public record RefreshTokenRequest(string AccessToken, string RefreshToken);

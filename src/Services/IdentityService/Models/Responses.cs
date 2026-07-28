namespace IdentityService.Models;

public record AuthResponse(string AccessToken, string RefreshToken, DateTime Expiration);

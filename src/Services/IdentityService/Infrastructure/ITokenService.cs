using System.Security.Claims;
using IdentityService.Domain;

namespace IdentityService.Infrastructure;

public interface ITokenService
{
    string GenerateAccessToken(ApplicationUser user, IList<string> roles);
    string GenerateRefreshToken();
    ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
}

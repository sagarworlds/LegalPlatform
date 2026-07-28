using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using UserService.Infrastructure;

namespace UserService.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/users");

        group.MapGet("/{id:guid}", GetProfileAsync);
        group.MapPut("/{id:guid}", UpdateProfileAsync);

        return endpoints;
    }

    private static async Task<IResult> GetProfileAsync(Guid id, UserDbContext dbContext)
    {
        var profile = await dbContext.UserProfiles.FindAsync(id);
        if (profile == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(profile);
    }

    public record UpdateProfileRequest(string FirstName, string LastName, string? PhoneNumber, string? AvatarUrl);

    private static async Task<IResult> UpdateProfileAsync(Guid id, UpdateProfileRequest request, UserDbContext dbContext)
    {
        var profile = await dbContext.UserProfiles.FindAsync(id);
        if (profile == null)
        {
            return Results.NotFound();
        }

        profile.FirstName = request.FirstName;
        profile.LastName = request.LastName;
        profile.PhoneNumber = request.PhoneNumber;
        profile.AvatarUrl = request.AvatarUrl;

        await dbContext.SaveChangesAsync();

        return Results.Ok(profile);
    }
}

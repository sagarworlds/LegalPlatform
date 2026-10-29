using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OrganizationService.Domain;
using OrganizationService.Infrastructure;
using OrganizationService.Models;
using EventBus.Abstractions;
using Contracts;
using ServiceDefaults.Security;

namespace OrganizationService.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // These endpoints were anonymous: anyone could create tenants and read any organization by id.
        var group = endpoints.MapGroup("/api/v1/organizations").RequireAuthorization(Policies.AuthenticatedUser);

        // Creating an organization creates a tenant, so it is a platform-level operation.
        group.MapPost("/", CreateOrganizationAsync)
            .RequireAuthorization(new AuthorizeAttribute { Roles = Roles.SystemAdmin });
        group.MapGet("/{id:guid}", GetOrganizationAsync);

        return endpoints;
    }

    public record CreateOrganizationRequest(string Name, string TenantId, string Domain);

    internal static async Task<IResult> CreateOrganizationAsync(
        CreateOrganizationRequest request,
        OrganizationDbContext dbContext,
        IEventBus eventBus)
    {
        var exists = await dbContext.Organizations.AnyAsync(x => x.TenantId == request.TenantId);
        if (exists)
        {
            return Results.BadRequest(new { Message = $"Tenant ID {request.TenantId} is already in use." });
        }

        var organization = new Organization(
            Guid.NewGuid(),
            request.Name,
            request.TenantId,
            request.Domain
        );

        dbContext.Organizations.Add(organization);
        await dbContext.SaveChangesAsync();

        await eventBus.PublishAsync(new OrganizationCreatedIntegrationEvent
        {
            OrganizationId = organization.Id,
            Name = organization.Name,
            TenantId = organization.TenantId
        });

        return Results.Created($"/api/v1/organizations/{organization.Id}", OrganizationResponse.From(organization));
    }

    /// <summary>A user's <c>organizationId</c> claim is the organization's Id, so members may read their own organization and SystemAdmin any.</summary>
    internal static async Task<IResult> GetOrganizationAsync(Guid id, UserContext user, OrganizationDbContext dbContext)
    {
        if (!user.IsInRole(Roles.SystemAdmin) && user.TenantId != id)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Not a member of this organization");
        }

        var organization = await dbContext.Organizations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (organization == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(OrganizationResponse.From(organization));
    }
}

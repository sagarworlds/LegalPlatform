using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OrganizationService.Domain;
using OrganizationService.Infrastructure;
using EventBus.Abstractions;
using Contracts;

namespace OrganizationService.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/organizations");

        group.MapPost("/", CreateOrganizationAsync);
        group.MapGet("/{id:guid}", GetOrganizationAsync);

        return endpoints;
    }

    public record CreateOrganizationRequest(string Name, string TenantId, string Domain);

    private static async Task<IResult> CreateOrganizationAsync(
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

        return Results.Created($"/api/v1/organizations/{organization.Id}", organization);
    }

    private static async Task<IResult> GetOrganizationAsync(Guid id, OrganizationDbContext dbContext)
    {
        var organization = await dbContext.Organizations.FindAsync(id);
        if (organization == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(organization);
    }
}

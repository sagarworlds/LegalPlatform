using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrganizationService.Endpoints;
using OrganizationService.Infrastructure;
using EventBus;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// PostgreSQL Database integration via Aspire resource connection
builder.AddNpgsqlDbContext<OrganizationDbContext>("organization-db");

// EventBus for publishing integration events
builder.Services.AddEventBus(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapOrganizationEndpoints();

app.Run();

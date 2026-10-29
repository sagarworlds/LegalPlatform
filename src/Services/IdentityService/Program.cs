using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IdentityService.Domain;
using IdentityService.Endpoints;
using IdentityService.Infrastructure;
using EventBus;
using ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// PostgreSQL Database integration via Aspire resource connection
builder.AddNpgsqlDbContext<IdentityDbContext>("identity-db");

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
})
    .AddEntityFrameworkStores<IdentityDbContext>()
    .AddDefaultTokenProviders();

// Registered after AddIdentity so bearer authentication, not Identity's cookie scheme, is the default.
// Needed to read the caller's token on /register and to authorize the user administration endpoints.
builder.Services.AddPlatformJwtAuthentication(builder.Configuration);

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IdentityDataSeeder>();

builder.Services.AddEventBus(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    // Seeds every role in ServiceDefaults.Security.Roles and the optional BootstrapAdmin account.
    await scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>().SeedAsync();
}

app.MapIdentityEndpoints();
app.MapUserAdministrationEndpoints();

app.Run();

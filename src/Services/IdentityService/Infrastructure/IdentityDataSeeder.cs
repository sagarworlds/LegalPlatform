using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using IdentityService.Domain;
using ServiceDefaults.Security;

namespace IdentityService.Infrastructure;

/// <summary>
/// Seeds the reference data Identity needs to be usable: every role in <see cref="Roles"/> and, when configured,
/// a bootstrap SystemAdmin. Without a first administrator there is no way to assign roles, so the audit trail and
/// every other admin-only feature would be unreachable.
/// </summary>
public sealed class IdentityDataSeeder
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentityDataSeeder> _logger;

    public IdentityDataSeeder(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger<IdentityDataSeeder> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
    }

    /// <exception cref="InvalidOperationException">A role or the bootstrap administrator could not be created.</exception>
    public async Task SeedAsync()
    {
        await SeedRolesAsync();
        await SeedBootstrapAdminAsync();
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in Roles.All)
        {
            if (await _roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await _roleManager.CreateAsync(new ApplicationRole(role));
            ThrowIfFailed(result, $"create role '{role}'");
        }
    }

    /// <summary>
    /// Creates the administrator described by the <c>BootstrapAdmin</c> configuration section (Email, Password and an
    /// optional OrganizationId). It is only configured in appsettings.Development.json; with no section there is nothing to seed.
    /// An existing account keeps its password and is only ensured to hold the SystemAdmin role.
    /// </summary>
    private async Task SeedBootstrapAdminAsync()
    {
        var section = _configuration.GetSection("BootstrapAdmin");
        var email = section["Email"];
        var password = section["Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogInformation("No BootstrapAdmin configured; skipping administrator seeding.");
            return;
        }

        var admin = await _userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            Guid? organizationId = Guid.TryParse(section["OrganizationId"], out var parsed) ? parsed : null;

            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = section["FirstName"] ?? "System",
                LastName = section["LastName"] ?? "Administrator",
                OrganizationId = organizationId,
                SecurityStamp = Guid.NewGuid().ToString()
            };

            ThrowIfFailed(await _userManager.CreateAsync(admin, password), $"create bootstrap admin '{email}'");
            _logger.LogInformation("Created bootstrap administrator {Email}.", email);
        }

        foreach (var role in new[] { Roles.SystemAdmin, Roles.User })
        {
            if (!await _userManager.IsInRoleAsync(admin, role))
            {
                ThrowIfFailed(await _userManager.AddToRoleAsync(admin, role), $"add '{email}' to role '{role}'");
            }
        }
    }

    private static void ThrowIfFailed(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
            throw new InvalidOperationException($"Failed to {operation}: {errors}");
        }
    }
}

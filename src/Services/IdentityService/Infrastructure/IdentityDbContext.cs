using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using IdentityService.Domain;

namespace IdentityService.Infrastructure;

public class IdentityDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable(name: "Users");
        });

        builder.Entity<ApplicationRole>(entity =>
        {
            entity.ToTable(name: "Roles");
        });
    }
}

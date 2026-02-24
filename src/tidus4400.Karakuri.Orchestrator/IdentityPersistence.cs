using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace tidus4400.Karakuri.Orchestrator;

public sealed class IdentityAppUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuthIdentityDbContext(DbContextOptions<AuthIdentityDbContext> options)
    : IdentityDbContext<IdentityAppUser, IdentityRole, string>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<IdentityAppUser>(b =>
        {
            b.Property(x => x.CreatedAt);
            b.HasIndex(x => x.Email);
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AutomationPlatform.Orchestrator;

public sealed class AuthIdentityDbContextFactory : IDesignTimeDbContextFactory<AuthIdentityDbContext>
{
    public AuthIdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuthIdentityDbContext>();
        var connectionString = "Host=localhost;Port=5432;Database=automationplatform;Username=automation;Password=automation";
        optionsBuilder.UseNpgsql(connectionString);
        return new AuthIdentityDbContext(optionsBuilder.Options);
    }
}

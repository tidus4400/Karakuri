using AutomationPlatform.Orchestrator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutomationPlatform.Orchestrator.Tests;

internal sealed class TestOrchestratorFactory : WebApplicationFactory<Program>
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "automationplatform-tests", Guid.NewGuid().ToString("N"));
    private string AuthDbPath => Path.Combine(_tempDir, "auth.db");
    internal string StorePath => Path.Combine(_tempDir, "store.json");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["Auth:DbInitMode"] = "EnsureCreated",
                ["Store:FilePath"] = StorePath,
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={AuthDbPath}"
            };
            config.AddInMemoryCollection(values);
        });
        Directory.CreateDirectory(_tempDir);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AuthIdentityDbContext>));
            services.RemoveAll(typeof(DbContextOptions<AuthIdentityDbContext>));
            services.RemoveAll(typeof(AuthIdentityDbContext));

            services.AddDbContext<AuthIdentityDbContext>(options =>
                options.UseSqlite($"Data Source={AuthDbPath}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Temp cleanup is best-effort for test runs.
        }
    }
}

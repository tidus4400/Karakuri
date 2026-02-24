using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace tidus4400.Karakuri.Orchestrator;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<BlockEntity> Blocks => Set<BlockEntity>();
    public DbSet<FlowEntity> Flows => Set<FlowEntity>();
    public DbSet<FlowVersionEntity> FlowVersions => Set<FlowVersionEntity>();
    public DbSet<RunnerAgentEntity> RunnerAgents => Set<RunnerAgentEntity>();
    public DbSet<RegistrationTokenEntity> RegistrationTokens => Set<RegistrationTokenEntity>();
    public DbSet<JobEntity> Jobs => Set<JobEntity>();
    public DbSet<JobStepEntity> JobSteps => Set<JobStepEntity>();
    public DbSet<JobLogEntity> JobLogs => Set<JobLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BlockEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.OwnerUserId).HasMaxLength(256);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.SchemaJson);
            b.HasIndex(x => new { x.OwnerUserId, x.Name });
        });

        modelBuilder.Entity<FlowEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.OwnerUserId).HasMaxLength(256);
            b.Property(x => x.Name).HasMaxLength(200);
            b.HasIndex(x => new { x.OwnerUserId, x.UpdatedAt });
        });

        modelBuilder.Entity<FlowVersionEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.FlowId, x.VersionNumber }).IsUnique();
            b.HasOne<FlowEntity>().WithMany().HasForeignKey(x => x.FlowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RunnerAgentEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Os).HasMaxLength(200);
            b.Property(x => x.SecretHash).HasMaxLength(128);
            b.Property(x => x.SecretValue).HasMaxLength(512);
            b.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<RegistrationTokenEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenHash).HasMaxLength(128);
            b.Property(x => x.CreatedByUserId).HasMaxLength(256);
            b.HasIndex(x => x.TokenHash).IsUnique();
        });

        modelBuilder.Entity<JobEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.QueuedAt);
            b.HasIndex(x => new { x.AgentId, x.Status, x.QueuedAt });
            b.HasOne<FlowEntity>().WithMany().HasForeignKey(x => x.FlowId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<FlowVersionEntity>().WithMany().HasForeignKey(x => x.FlowVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JobStepEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.JobId, x.NodeId }).IsUnique();
            b.HasOne<JobEntity>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobLogEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Message);
            b.HasIndex(x => new { x.JobId, x.Id });
            b.HasOne<JobEntity>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PlatformDbContext>();
        var connectionString = "Host=localhost;Port=5432;Database=karakuri;Username=automation;Password=automation";
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistoryPlatform");
        });
        return new PlatformDbContext(optionsBuilder.Options);
    }
}

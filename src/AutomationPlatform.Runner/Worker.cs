namespace AutomationPlatform.Runner;

public sealed class Worker(RunnerEngine engine, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Runner worker starting");
        await engine.RunAsync(stoppingToken);
    }
}

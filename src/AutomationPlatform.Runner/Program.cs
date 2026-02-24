using System.Text.Json;
using AutomationPlatform.Runner;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RunnerOptions>(builder.Configuration.GetSection("Runner"));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var localPort = builder.Configuration.GetValue<int?>("Runner:LocalPort") ?? 5180;
builder.WebHost.UseUrls($"http://0.0.0.0:{localPort}");

builder.Services.AddSingleton<RunnerState>();
builder.Services.AddSingleton<RunnerCredentialStore>();
builder.Services.AddHttpClient<OrchestratorClient>();
builder.Services.AddSingleton<RunProcessExecutor>();
builder.Services.AddSingleton<RunnerEngine>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));

app.MapGet("/version", () =>
{
    var assembly = typeof(Program).Assembly;
    var version = assembly.GetName().Version?.ToString() ?? "0.0.0";
    var informational = assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion;
    return Results.Ok(new
    {
        version,
        informationalVersion = informational ?? version
    });
});

app.MapGet("/status", (RunnerState state, Microsoft.Extensions.Options.IOptions<RunnerOptions> options) => Results.Ok(state.CreateSnapshot(options.Value)));

app.Run();

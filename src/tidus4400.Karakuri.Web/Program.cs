using tidus4400.Karakuri.Web.Components;
using tidus4400.Karakuri.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<ApiSessionState>();
builder.Services.AddScoped<MonitoringHubClient>();
builder.Services.AddScoped<OrchestratorApiClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Orchestrator:BaseUrl"] ?? "http://localhost:5010/";
    var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    return new OrchestratorApiClient(client, sp.GetRequiredService<ApiSessionState>());
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

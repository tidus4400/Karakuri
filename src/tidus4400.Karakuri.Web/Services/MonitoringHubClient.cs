using tidus4400.Karakuri.Shared;
using Microsoft.AspNetCore.SignalR.Client;

namespace tidus4400.Karakuri.Web.Services;

public sealed class MonitoringHubClient : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ApiSessionState _session;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HubConnection? _connection;
    private string? _connectedUserId;

    public event Action<RunnerDto>? RunnerUpdated;
    public event Action<JobDto>? JobUpdated;
    public event Action<Guid, JobLogDto>? JobLogAppended;

    public MonitoringHubClient(IConfiguration configuration, ApiSessionState session)
    {
        _configuration = configuration;
        _session = session;
        _session.Changed += OnSessionChanged;
    }

    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated || _session.CurrentUser is null)
        {
            await StopAsync();
            return;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is not null && _connection.State == HubConnectionState.Connected && _connectedUserId == _session.CurrentUser.UserId)
            {
                return;
            }

            await StopConnectionInternalAsync();

            var baseUrl = (_configuration["Orchestrator:BaseUrl"] ?? "http://localhost:5010").TrimEnd('/');
            var user = _session.CurrentUser;
            var connection = new HubConnectionBuilder()
                .WithUrl($"{baseUrl}/hubs/monitoring", options =>
                {
                    options.Headers.Add("X-User-Id", user.UserId);
                    options.Headers.Add("X-User-Email", user.Email);
                    options.Headers.Add("X-User-Role", user.Role);
                })
                .WithAutomaticReconnect()
                .Build();

            connection.On<RunnerDto>("RunnerUpdated", dto => RunnerUpdated?.Invoke(dto));
            connection.On<JobDto>("JobUpdated", dto => JobUpdated?.Invoke(dto));
            connection.On<Guid, JobLogDto>("JobLogAppended", (jobId, log) => JobLogAppended?.Invoke(jobId, log));
            await connection.StartAsync(ct);

            _connection = connection;
            _connectedUserId = user.UserId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopConnectionInternalAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async void OnSessionChanged()
    {
        try
        {
            await EnsureStartedAsync();
        }
        catch
        {
            // Pages also do initial loads; ignore background reconnect failures.
        }
    }

    private async Task StopConnectionInternalAsync()
    {
        if (_connection is null) return;
        try
        {
            await _connection.StopAsync();
        }
        catch
        {
            // ignore shutdown failures
        }
        await _connection.DisposeAsync();
        _connection = null;
        _connectedUserId = null;
    }

    public async ValueTask DisposeAsync()
    {
        _session.Changed -= OnSessionChanged;
        await StopAsync();
        _gate.Dispose();
    }
}

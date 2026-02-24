using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using tidus4400.Karakuri.Shared;

namespace tidus4400.Karakuri.Web.Services;

public sealed class OrchestratorApiClient(HttpClient httpClient, ApiSessionState session)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var response = await SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login", request, ct);
        if (response.Success && response.User is not null)
        {
            session.SetUser(response.User);
        }
        return response;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await SendRawAsync(HttpMethod.Post, "api/auth/logout", null, ct);
        }
        catch
        {
            // Best effort for local dev.
        }
        session.SetUser(null);
    }

    public Task<AuthUserDto> RegisterUserAsync(RegisterUserRequest request, CancellationToken ct = default)
        => SendAsync<AuthUserDto>(HttpMethod.Post, "api/auth/register", request, ct);

    public Task<List<BlockDto>> GetBlocksAsync(CancellationToken ct = default)
        => SendAsync<List<BlockDto>>(HttpMethod.Get, "api/blocks", null, ct);

    public Task<BlockDto> CreateBlockAsync(UpsertBlockRequest request, CancellationToken ct = default)
        => SendAsync<BlockDto>(HttpMethod.Post, "api/blocks", request, ct);

    public Task<BlockDto> UpdateBlockAsync(Guid id, UpsertBlockRequest request, CancellationToken ct = default)
        => SendAsync<BlockDto>(HttpMethod.Put, $"api/blocks/{id}", request, ct);

    public Task DeleteBlockAsync(Guid id, CancellationToken ct = default)
        => SendNoContentAsync(HttpMethod.Delete, $"api/blocks/{id}", ct);

    public Task<List<FlowSummaryDto>> GetFlowsAsync(CancellationToken ct = default)
        => SendAsync<List<FlowSummaryDto>>(HttpMethod.Get, "api/flows", null, ct);

    public Task<FlowDto> CreateFlowAsync(CreateFlowRequest request, CancellationToken ct = default)
        => SendAsync<FlowDto>(HttpMethod.Post, "api/flows", request, ct);

    public Task<FlowDto> GetFlowAsync(Guid id, CancellationToken ct = default)
        => SendAsync<FlowDto>(HttpMethod.Get, $"api/flows/{id}", null, ct);

    public Task<FlowDto> UpdateFlowAsync(Guid id, UpdateFlowRequest request, CancellationToken ct = default)
        => SendAsync<FlowDto>(HttpMethod.Put, $"api/flows/{id}", request, ct);

    public Task<FlowVersionDto> GetLatestFlowVersionAsync(Guid flowId, CancellationToken ct = default)
        => SendAsync<FlowVersionDto>(HttpMethod.Get, $"api/flows/{flowId}/versions/latest", null, ct);

    public Task<FlowVersionDto> SaveFlowVersionAsync(Guid flowId, SaveFlowVersionRequest request, CancellationToken ct = default)
        => SendAsync<FlowVersionDto>(HttpMethod.Post, $"api/flows/{flowId}/versions", request, ct);

    public Task<JobDto> RunFlowAsync(Guid flowId, CancellationToken ct = default)
        => SendAsync<JobDto>(HttpMethod.Post, $"api/flows/{flowId}/run", new RunFlowRequest(), ct);

    public Task<List<JobDto>> GetJobsAsync(CancellationToken ct = default)
        => SendAsync<List<JobDto>>(HttpMethod.Get, "api/jobs", null, ct);

    public Task<JobDetailsDto> GetJobAsync(Guid id, CancellationToken ct = default)
        => SendAsync<JobDetailsDto>(HttpMethod.Get, $"api/jobs/{id}", null, ct);

    public Task<List<JobLogDto>> GetJobLogsAsync(Guid id, int skip, int take, CancellationToken ct = default)
        => SendAsync<List<JobLogDto>>(HttpMethod.Get, $"api/jobs/{id}/logs?skip={skip}&take={take}", null, ct);

    public Task<JobDto> CancelJobAsync(Guid id, CancellationToken ct = default)
        => SendAsync<JobDto>(HttpMethod.Post, $"api/jobs/{id}/cancel", new { }, ct);

    public Task<List<RunnerDto>> GetRunnersAsync(CancellationToken ct = default)
        => SendAsync<List<RunnerDto>>(HttpMethod.Get, "api/runners", null, ct);

    public Task<CreateRegistrationTokenResponse> CreateTokenAsync(CancellationToken ct = default)
        => SendAsync<CreateRegistrationTokenResponse>(HttpMethod.Post, "api/tokens", new { }, ct);

    public Task<List<RegistrationTokenDto>> GetTokensAsync(CancellationToken ct = default)
        => SendAsync<List<RegistrationTokenDto>>(HttpMethod.Get, "api/tokens", null, ct);

    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct = default)
    {
        using var response = await SendRawAsync(method, path, body, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildErrorAsync(response, ct), null, response.StatusCode);
        }

        if (response.Content.Headers.ContentLength == 0)
        {
            return default!;
        }

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return value!;
    }

    public async Task SendNoContentAsync(HttpMethod method, string path, CancellationToken ct = default)
    {
        using var response = await SendRawAsync(method, path, null, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await BuildErrorAsync(response, ct), null, response.StatusCode);
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        if (session.CurrentUser is { } user)
        {
            request.Headers.Remove("X-User-Id");
            request.Headers.Remove("X-User-Email");
            request.Headers.Remove("X-User-Role");
            request.Headers.Add("X-User-Id", user.UserId);
            request.Headers.Add("X-User-Email", user.Email);
            request.Headers.Add("X-User-Role", user.Role);
        }

        return await httpClient.SendAsync(request, ct);
    }

    private static async Task<string> BuildErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"HTTP {(int)response.StatusCode} ({response.StatusCode})";
        }
        return $"HTTP {(int)response.StatusCode} ({response.StatusCode}): {body}";
    }
}

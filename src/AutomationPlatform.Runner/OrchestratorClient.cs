using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutomationPlatform.Shared;
using Microsoft.Extensions.Options;

namespace AutomationPlatform.Runner;

public sealed class OrchestratorClient(HttpClient httpClient, IOptions<RunnerOptions> options)
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private Uri BuildUri(string relativeOrAbsolute)
    {
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        var baseUrl = options.Value.ServerUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(baseUrl), relativeOrAbsolute.TrimStart('/'));
    }

    public async Task<AgentRegisterResponse> RegisterAsync(string registrationToken, string? name, string? tags, CancellationToken ct)
    {
        var os = System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim();
        var response = await httpClient.PostAsJsonAsync(
            BuildUri("/api/agents/register"),
            new AgentRegisterRequest
            {
                RegistrationToken = registrationToken,
                Name = string.IsNullOrWhiteSpace(name) ? Environment.MachineName : name!,
                Os = os,
                Tags = tags
            },
            _json,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Runner registration failed ({(int)response.StatusCode}): {body}");
        }

        return (await response.Content.ReadFromJsonAsync<AgentRegisterResponse>(_json, ct))
            ?? throw new InvalidOperationException("Runner registration returned empty payload.");
    }

    public async Task<RunnerDto?> HeartbeatAsync(RunnerCredentials credentials, int runningJobs, CancellationToken ct)
    {
        var path = $"/api/agents/{credentials.AgentId}/heartbeat";
        var body = new HeartbeatRequest
        {
            RunningJobs = runningJobs,
            StatusMessage = runningJobs > 0 ? "busy" : "idle"
        };

        var request = CreateSignedJsonRequest(HttpMethod.Post, path, credentials.AgentId, credentials.AgentSecret, body);
        var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Runner heartbeat unauthorized.");
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RunnerDto>(_json, ct);
    }

    public async Task<JobExecutionPayloadDto?> PullNextJobAsync(RunnerCredentials credentials, int waitSeconds, CancellationToken ct)
    {
        var path = $"/api/agents/{credentials.AgentId}/jobs/next?waitSeconds={Math.Clamp(waitSeconds, 1, 45)}";
        var request = CreateSignedRequest(HttpMethod.Get, path, credentials.AgentId, credentials.AgentSecret);
        var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Runner pull unauthorized.");
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobExecutionPayloadDto>(_json, ct);
    }

    public async Task PostEventsAsync(RunnerCredentials credentials, Guid jobId, IEnumerable<JobEventDto> events, CancellationToken ct)
    {
        var payload = new JobEventsRequest { Events = events.ToList() };
        if (payload.Events.Count == 0) return;
        var path = $"/api/jobs/{jobId}/events";
        var request = CreateSignedJsonRequest(HttpMethod.Post, path, credentials.AgentId, credentials.AgentSecret, payload);
        var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Runner events unauthorized.");
        }
        response.EnsureSuccessStatusCode();
    }

    public async Task<JobCancelStatusDto> GetCancelStatusAsync(RunnerCredentials credentials, Guid jobId, CancellationToken ct)
    {
        var path = $"/api/jobs/{jobId}/cancel-status";
        var request = CreateSignedRequest(HttpMethod.Get, path, credentials.AgentId, credentials.AgentSecret);
        var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Runner cancel-status unauthorized.");
        }
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JobCancelStatusDto>(_json, ct))
            ?? throw new InvalidOperationException("Cancel status payload was empty.");
    }

    public async Task CompleteJobAsync(RunnerCredentials credentials, Guid jobId, string? resultSummary, CancellationToken ct)
    {
        var path = $"/api/jobs/{jobId}/complete";
        var request = CreateSignedJsonRequest(
            HttpMethod.Post,
            path,
            credentials.AgentId,
            credentials.AgentSecret,
            new JobCompleteRequest { ResultSummary = resultSummary });
        var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Runner complete unauthorized.");
        }
        response.EnsureSuccessStatusCode();
    }

    public async Task CancelJobAsync(RunnerCredentials credentials, Guid jobId, string? reason, CancellationToken ct)
    {
        var path = $"/api/jobs/{jobId}/canceled";
        var request = CreateSignedJsonRequest(
            HttpMethod.Post,
            path,
            credentials.AgentId,
            credentials.AgentSecret,
            new JobCanceledRequest { Reason = reason });
        var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Runner canceled unauthorized.");
        }
        response.EnsureSuccessStatusCode();
    }

    public async Task FailJobAsync(RunnerCredentials credentials, Guid jobId, string error, CancellationToken ct)
    {
        var path = $"/api/jobs/{jobId}/fail";
        var request = CreateSignedJsonRequest(
            HttpMethod.Post,
            path,
            credentials.AgentId,
            credentials.AgentSecret,
            new JobFailRequest { Error = error });
        var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Runner fail unauthorized.");
        }
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateSignedRequest(HttpMethod method, string pathAndQuery, Guid agentId, string agentSecret)
    {
        var uri = BuildUri(pathAndQuery);
        var request = new HttpRequestMessage(method, uri);
        var bodyBytes = Array.Empty<byte>();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyHash = HmacSigning.ComputeBodySha256Hex(bodyBytes);
        var canonical = HmacSigning.BuildCanonicalString(method.Method, uri.AbsolutePath, ts, bodyHash);
        var signature = HmacSigning.ComputeSignatureBase64(agentSecret, canonical);
        request.Headers.Add("X-Agent-Id", agentId.ToString());
        request.Headers.Add("X-Timestamp", ts);
        request.Headers.Add("X-Signature", signature);
        return request;
    }

    private HttpRequestMessage CreateSignedJsonRequest<T>(HttpMethod method, string pathAndQuery, Guid agentId, string agentSecret, T? body)
    {
        var uri = BuildUri(pathAndQuery);
        var request = new HttpRequestMessage(method, uri);

        byte[] bodyBytes = Array.Empty<byte>();
        if (body is not null)
        {
            bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body, _json);
            request.Content = new ByteArrayContent(bodyBytes);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        }

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyHash = HmacSigning.ComputeBodySha256Hex(bodyBytes);
        var canonical = HmacSigning.BuildCanonicalString(method.Method, uri.AbsolutePath, ts, bodyHash);
        var signature = HmacSigning.ComputeSignatureBase64(agentSecret, canonical);

        request.Headers.Add("X-Agent-Id", agentId.ToString());
        request.Headers.Add("X-Timestamp", ts);
        request.Headers.Add("X-Signature", signature);
        return request;
    }
}

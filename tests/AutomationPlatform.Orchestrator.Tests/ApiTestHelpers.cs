using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AutomationPlatform.Shared;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AutomationPlatform.Orchestrator.Tests;

internal static class ApiTestHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static HttpClient CreateClient(this TestOrchestratorFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public static async Task<AuthUserDto> LoginAsync(this HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        Assert.NotNull(payload);
        Assert.True(payload!.Success);
        Assert.NotNull(payload.User);
        return payload.User!;
    }

    public static async Task<CreateRegistrationTokenResponse> CreateTokenAsync(this HttpClient adminClient)
    {
        var response = await adminClient.PostAsync("/api/tokens", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateRegistrationTokenResponse>(JsonOptions))!;
    }

    public static async Task<AgentRegisterResponse> RegisterAgentAsync(this HttpClient client, string token, string name = "runner-test")
    {
        var response = await client.PostAsJsonAsync("/api/agents/register", new AgentRegisterRequest
        {
            RegistrationToken = token,
            Name = name,
            Os = "test-os",
            Tags = "ci,test"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AgentRegisterResponse>(JsonOptions))!;
    }

    public static async Task<FlowDto> CreateFlowAsync(this HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/flows", new CreateFlowRequest { Name = name, Description = "test" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FlowDto>(JsonOptions))!;
    }

    public static HttpRequestMessage CreateSignedRequest(HttpMethod method, string pathAndQuery, Guid agentId, string secretBase64, object? body = null)
    {
        var request = new HttpRequestMessage(method, pathAndQuery);
        var bodyBytes = Array.Empty<byte>();

        if (body is not null)
        {
            bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
            request.Content = new ByteArrayContent(bodyBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        var pathOnly = new Uri("http://localhost" + pathAndQuery).AbsolutePath;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyHash = HmacSigning.ComputeBodySha256Hex(bodyBytes);
        var canonical = HmacSigning.BuildCanonicalString(method.Method, pathOnly, ts, bodyHash);
        var signature = HmacSigning.ComputeSignatureBase64(secretBase64, canonical);

        request.Headers.Add("X-Agent-Id", agentId.ToString());
        request.Headers.Add("X-Timestamp", ts);
        request.Headers.Add("X-Signature", signature);
        return request;
    }

    public static HttpRequestMessage CreateSignedRequestWithSignature(HttpMethod method, string pathAndQuery, Guid agentId, string signatureBase64, object? body = null, long? timestamp = null)
    {
        var request = new HttpRequestMessage(method, pathAndQuery);
        if (body is not null)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        request.Headers.Add("X-Agent-Id", agentId.ToString());
        request.Headers.Add("X-Timestamp", (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString());
        request.Headers.Add("X-Signature", signatureBase64);
        return request;
    }

    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        Assert.NotNull(payload);
        return payload!;
    }
}

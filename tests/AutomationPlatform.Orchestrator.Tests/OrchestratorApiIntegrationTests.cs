using System.Net;
using System.Net.Http.Json;
using AutomationPlatform.Orchestrator;
using AutomationPlatform.Shared;

namespace AutomationPlatform.Orchestrator.Tests;

public sealed class OrchestratorApiIntegrationTests
{
    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        using var factory = new TestOrchestratorFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SeededAuth_LoginAndMe_WorkForAdminAndUser()
    {
        using var factory = new TestOrchestratorFactory();
        using var adminClient = factory.CreateClient();
        using var userClient = factory.CreateClient();

        var admin = await adminClient.LoginAsync("admin@local", "Admin123!");
        var user = await userClient.LoginAsync("user@local", "User123!");

        var adminMe = await adminClient.GetAsync("/api/auth/me");
        var userMe = await userClient.GetAsync("/api/auth/me");

        Assert.Equal("Admin", (await adminMe.ReadJsonAsync<AuthUserDto>()).Role);
        Assert.Equal("User", (await userMe.ReadJsonAsync<AuthUserDto>()).Role);
        Assert.NotEqual(admin.UserId, user.UserId);
    }

    [Fact]
    public async Task UserEndpoints_AreScoped_AndAdminOnlyEndpointsAreForbiddenForUser()
    {
        using var factory = new TestOrchestratorFactory();
        using var adminClient = factory.CreateClient();
        using var userClient = factory.CreateClient();

        await adminClient.LoginAsync("admin@local", "Admin123!");
        await userClient.LoginAsync("user@local", "User123!");

        var userFlow = await userClient.CreateFlowAsync("User flow");
        var adminFlow = await adminClient.CreateFlowAsync("Admin flow");

        var userFlows = await (await userClient.GetAsync("/api/flows")).ReadJsonAsync<List<FlowSummaryDto>>();
        var adminFlows = await (await adminClient.GetAsync("/api/flows")).ReadJsonAsync<List<FlowSummaryDto>>();
        var userTokenAttempt = await userClient.PostAsync("/api/tokens", null);
        var userRunnersAttempt = await userClient.GetAsync("/api/runners");

        Assert.Contains(userFlows, f => f.Id == userFlow.Id);
        Assert.DoesNotContain(userFlows, f => f.Id == adminFlow.Id);
        Assert.Contains(adminFlows, f => f.Id == userFlow.Id);
        Assert.Contains(adminFlows, f => f.Id == adminFlow.Id);
        Assert.Equal(HttpStatusCode.Forbidden, userTokenAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, userRunnersAttempt.StatusCode);
    }

    [Fact]
    public async Task RunnerRegistrationAndHeartbeat_RequireValidHmac()
    {
        using var factory = new TestOrchestratorFactory();
        using var adminClient = factory.CreateClient();
        using var client = factory.CreateClient();

        await adminClient.LoginAsync("admin@local", "Admin123!");
        var token = await adminClient.CreateTokenAsync();
        var agent = await client.RegisterAgentAsync(token.Token);

        var badHeartbeat = ApiTestHelpers.CreateSignedRequestWithSignature(
            HttpMethod.Post,
            $"/api/agents/{agent.AgentId}/heartbeat",
            agent.AgentId,
            signatureBase64: Convert.ToBase64String(new byte[32]),
            body: new HeartbeatRequest { RunningJobs = 0 });

        var badResponse = await client.SendAsync(badHeartbeat);
        Assert.Equal(HttpStatusCode.Unauthorized, badResponse.StatusCode);

        var okHeartbeat = ApiTestHelpers.CreateSignedRequest(
            HttpMethod.Post,
            $"/api/agents/{agent.AgentId}/heartbeat",
            agent.AgentId,
            agent.AgentSecret,
            new HeartbeatRequest { RunningJobs = 1 });

        var okResponse = await client.SendAsync(okHeartbeat);
        var runner = await okResponse.ReadJsonAsync<RunnerDto>();

        Assert.Equal(RunnerStatus.Busy, runner.Status);
    }

    [Fact]
    public async Task FlowRun_RunnerPull_Events_AndComplete_UpdateJobAndLogs()
    {
        using var factory = new TestOrchestratorFactory();
        using var adminClient = factory.CreateClient();
        using var runnerClient = factory.CreateClient();

        await adminClient.LoginAsync("admin@local", "Admin123!");

        var token = await adminClient.CreateTokenAsync();
        var agent = await runnerClient.RegisterAgentAsync(token.Token, "runner-e2e");

        var flow = await adminClient.CreateFlowAsync("Flow E2E");
        var versionResponse = await adminClient.PostAsJsonAsync($"/api/flows/{flow.Id}/versions", new SaveFlowVersionRequest
        {
            Definition = new FlowDefinition
            {
                Nodes =
                [
                    new NodeDto
                    {
                        NodeId = "n1",
                        BlockType = "RunProcess",
                        DisplayName = "Launch",
                        X = 200,
                        Y = 150,
                        Config = new Dictionary<string, object?> { ["path"] = "dotnet", ["args"] = "--info", ["timeoutSec"] = 30 }
                    }
                ]
            }
        });
        var version = await versionResponse.ReadJsonAsync<FlowVersionDto>();
        Assert.Equal(2, version.VersionNumber);
        Assert.Equal(200, version.Definition.Nodes[0].X);

        var runResponse = await adminClient.PostAsJsonAsync($"/api/flows/{flow.Id}/run", new RunFlowRequest());
        var job = await runResponse.ReadJsonAsync<JobDto>();
        Assert.Equal(flow.Id, job.FlowId);

        var pullRequest = ApiTestHelpers.CreateSignedRequest(
            HttpMethod.Get,
            $"/api/agents/{agent.AgentId}/jobs/next?waitSeconds=1",
            agent.AgentId,
            agent.AgentSecret);
        var pullResponse = await runnerClient.SendAsync(pullRequest);
        var payload = await pullResponse.ReadJsonAsync<JobExecutionPayloadDto>();

        Assert.Equal(job.Id, payload.JobId);
        Assert.Equal("Flow E2E", payload.FlowName);
        Assert.Single(payload.Definition.Nodes);

        var eventsRequest = ApiTestHelpers.CreateSignedRequest(
            HttpMethod.Post,
            $"/api/jobs/{job.Id}/events",
            agent.AgentId,
            agent.AgentSecret,
            new JobEventsRequest
            {
                Events =
                [
                    new JobEventDto
                    {
                        NodeId = "n1",
                        StepStatus = StepStatus.Running,
                        Message = "step started",
                        Level = LogLevelKind.Information
                    },
                    new JobEventDto
                    {
                        NodeId = "n1",
                        StepStatus = StepStatus.Succeeded,
                        ExitCode = 0,
                        OutputJson = """{"stdout":"ok"}""",
                        Message = "step done",
                        Level = LogLevelKind.Information
                    }
                ]
            });
        var eventsResponse = await runnerClient.SendAsync(eventsRequest);
        Assert.Equal(HttpStatusCode.OK, eventsResponse.StatusCode);

        var completeRequest = ApiTestHelpers.CreateSignedRequest(
            HttpMethod.Post,
            $"/api/jobs/{job.Id}/complete",
            agent.AgentId,
            agent.AgentSecret,
            new JobCompleteRequest { ResultSummary = "Completed in test" });
        var completeResponse = await runnerClient.SendAsync(completeRequest);
        var completedJob = await completeResponse.ReadJsonAsync<JobDto>();
        Assert.Equal(JobStatus.Succeeded, completedJob.Status);
        Assert.Equal("Completed in test", completedJob.ResultSummary);

        var details = await (await adminClient.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobDetailsDto>();
        var pagedLogs = await (await adminClient.GetAsync($"/api/jobs/{job.Id}/logs?skip=0&take=10")).ReadJsonAsync<List<JobLogDto>>();

        Assert.Equal(JobStatus.Succeeded, details.Job.Status);
        Assert.True(details.Job.DurationMs >= 0);
        var step = Assert.Single(details.Steps);
        Assert.Equal("n1", step.NodeId);
        Assert.Equal(StepStatus.Succeeded, step.Status);
        Assert.Equal(0, step.ExitCode);
        Assert.Contains(details.Logs, l => l.Message == "step started");
        Assert.Contains(details.Logs, l => l.Message == "step done");
        Assert.True(pagedLogs.Count >= 2);
    }

    [Fact]
    public async Task CancelRequest_RunnerPollsCancelStatus_AndMarksJobCanceled()
    {
        using var factory = new TestOrchestratorFactory();
        using var adminClient = factory.CreateClient();
        using var runnerClient = factory.CreateClient();

        await adminClient.LoginAsync("admin@local", "Admin123!");

        var token = await adminClient.CreateTokenAsync();
        var agent = await runnerClient.RegisterAgentAsync(token.Token, "runner-cancel");

        var flow = await adminClient.CreateFlowAsync("Flow Cancel");
        var runResponse = await adminClient.PostAsJsonAsync($"/api/flows/{flow.Id}/run", new RunFlowRequest());
        var job = await runResponse.ReadJsonAsync<JobDto>();

        var pullRequest = ApiTestHelpers.CreateSignedRequest(
            HttpMethod.Get,
            $"/api/agents/{agent.AgentId}/jobs/next?waitSeconds=1",
            agent.AgentId,
            agent.AgentSecret);
        var pullResponse = await runnerClient.SendAsync(pullRequest);
        var payload = await pullResponse.ReadJsonAsync<JobExecutionPayloadDto>();
        Assert.Equal(job.Id, payload.JobId);

        var cancelResponse = await adminClient.PostAsync($"/api/jobs/{job.Id}/cancel", null);
        var canceledRequestedJob = await cancelResponse.ReadJsonAsync<JobDto>();
        Assert.True(canceledRequestedJob.CancelRequested);

        var cancelStatusRequest = ApiTestHelpers.CreateSignedRequest(
            HttpMethod.Get,
            $"/api/jobs/{job.Id}/cancel-status",
            agent.AgentId,
            agent.AgentSecret);
        var cancelStatusResponse = await runnerClient.SendAsync(cancelStatusRequest);
        var cancelStatus = await cancelStatusResponse.ReadJsonAsync<JobCancelStatusDto>();
        Assert.True(cancelStatus.CancelRequested);

        var canceledCompleteRequest = ApiTestHelpers.CreateSignedRequest(
            HttpMethod.Post,
            $"/api/jobs/{job.Id}/canceled",
            agent.AgentId,
            agent.AgentSecret,
            new JobCanceledRequest { Reason = "Canceled during test" });
        var canceledCompleteResponse = await runnerClient.SendAsync(canceledCompleteRequest);
        var canceledJob = await canceledCompleteResponse.ReadJsonAsync<JobDto>();
        Assert.Equal(JobStatus.Canceled, canceledJob.Status);
        Assert.True(canceledJob.CancelRequested);

        var details = await (await adminClient.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobDetailsDto>();
        Assert.Equal(JobStatus.Canceled, details.Job.Status);
        Assert.True(details.Job.CancelRequested);
        Assert.Single(details.Steps);
        Assert.Equal(StepStatus.Canceled, details.Steps[0].Status);
        Assert.Contains(details.Logs, l => l.Message.Contains("Cancel requested", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(details.Logs, l => l.Message.Contains("Canceled during test", StringComparison.OrdinalIgnoreCase));
    }
}

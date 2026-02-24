using System.Text.Json;
using AutomationPlatform.Shared;
using Microsoft.Extensions.Options;

namespace AutomationPlatform.Runner;

public sealed class RunnerEngine(
    RunnerState state,
    RunnerCredentialStore credentialStore,
    OrchestratorClient orchestratorClient,
    RunProcessExecutor processExecutor,
    IOptions<RunnerOptions> options,
    ILogger<RunnerEngine> logger)
{
    private readonly RunnerOptions _options = options.Value;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            Task? heartbeatTask = null;
            try
            {
                var credentials = await EnsureRegisteredAsync(stoppingToken);

                heartbeatTask = Task.Run(() => HeartbeatLoopAsync(credentials, heartbeatCts.Token), CancellationToken.None);
                await PollAndExecuteLoopAsync(credentials, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (UnauthorizedAccessException ex)
            {
                state.RecordError(ex, "authorization");
                state.SetStatus("Auth failed; retrying registration in 5s");
                logger.LogWarning(ex, "Runner authorization failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                heartbeatCts.Cancel();
                if (heartbeatTask is not null)
                {
                    try { await heartbeatTask; } catch { }
                }
                heartbeatTask = null;
                if (!stoppingToken.IsCancellationRequested)
                {
                    // Force re-registration on next loop.
                    await TryDeleteCredentialsAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                state.RecordError(ex, "runner loop");
                state.SetStatus("Error; retrying in 5s");
                logger.LogError(ex, "Runner loop failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            finally
            {
                heartbeatCts.Cancel();
                if (heartbeatTask is not null)
                {
                    try { await heartbeatTask; } catch (Exception ex) { logger.LogDebug(ex, "Heartbeat loop finished"); }
                }
            }
        }
    }

    private async Task<RunnerCredentials> EnsureRegisteredAsync(CancellationToken ct)
    {
        var existing = await credentialStore.LoadAsync(ct);
        if (existing is not null && existing.AgentId != Guid.Empty && !string.IsNullOrWhiteSpace(existing.AgentSecret))
        {
            state.SetCredentials(existing);
            state.SetStatus("Registered");
            return existing;
        }

        if (string.IsNullOrWhiteSpace(_options.RegistrationToken))
        {
            state.SetStatus("Waiting for registration token");
            throw new InvalidOperationException("Runner registration token is required when no credential file exists.");
        }

        state.SetStatus("Registering");
        var registration = await orchestratorClient.RegisterAsync(_options.RegistrationToken, _options.Name, _options.Tags, ct);
        var credentials = new RunnerCredentials
        {
            AgentId = registration.AgentId,
            AgentSecret = registration.AgentSecret,
            RegisteredAt = DateTimeOffset.UtcNow,
            Name = string.IsNullOrWhiteSpace(_options.Name) ? Environment.MachineName : _options.Name
        };
        await credentialStore.SaveAsync(credentials, ct);
        state.SetCredentials(credentials);
        logger.LogInformation("Runner registered with agent id {AgentId}", credentials.AgentId);
        return credentials;
    }

    private async Task PollAndExecuteLoopAsync(RunnerCredentials credentials, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            state.MarkPoll(DateTimeOffset.UtcNow);
            state.SetStatus("Polling");
            var payload = await orchestratorClient.PullNextJobAsync(credentials, _options.LongPollWaitSeconds, ct);
            if (payload is null)
            {
                state.SetStatus("Idle");
                continue;
            }

            await ExecuteJobAsync(credentials, payload, ct);
        }
    }

    private async Task HeartbeatLoopAsync(RunnerCredentials credentials, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.HeartbeatSeconds, 5, 300));
        using var timer = new PeriodicTimer(interval);

        // Fire an initial heartbeat quickly.
        await SendHeartbeatSafeAsync(credentials, ct);

        while (await timer.WaitForNextTickAsync(ct))
        {
            await SendHeartbeatSafeAsync(credentials, ct);
        }
    }

    private async Task SendHeartbeatSafeAsync(RunnerCredentials credentials, CancellationToken ct)
    {
        try
        {
            await orchestratorClient.HeartbeatAsync(credentials, state.CreateSnapshot(_options).RunningJobs, ct);
            state.MarkHeartbeat(DateTimeOffset.UtcNow, "Heartbeat ok");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.RecordError(ex, "heartbeat");
            logger.LogWarning(ex, "Heartbeat failed");
        }
    }

    private async Task ExecuteJobAsync(RunnerCredentials credentials, JobExecutionPayloadDto payload, CancellationToken ct)
    {
        state.MarkJobStarted(payload.JobId, payload.FlowName);
        logger.LogInformation("Executing job {JobId} ({FlowName})", payload.JobId, payload.FlowName);

        try
        {
            var nodes = RunnerFlowOrdering.Order(payload.Definition);
            foreach (var node in nodes)
            {
                ct.ThrowIfCancellationRequested();

                var startEvents = new List<JobEventDto>
                {
                    new()
                    {
                        NodeId = node.NodeId,
                        BlockType = node.BlockType,
                        StepStatus = StepStatus.Running,
                        Message = $"Starting {node.DisplayName} ({node.BlockType})",
                        Level = LogLevelKind.Information
                    }
                };
                await orchestratorClient.PostEventsAsync(credentials, payload.JobId, startEvents, ct);

                if (!string.Equals(node.BlockType, "RunProcess", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsupported block type: {node.BlockType}");
                }

                var result = await processExecutor.ExecuteAsync(node, ct);

                var outputJson = JsonSerializer.Serialize(new
                {
                    result.ExitCode,
                    result.TimedOut,
                    result.StdOut,
                    result.StdErr,
                    result.StdOutTruncated,
                    result.StdErrTruncated,
                    result.DurationMs
                });

                var finishStatus = result.Succeeded ? StepStatus.Succeeded : StepStatus.Failed;
                var events = new List<JobEventDto>
                {
                    new()
                    {
                        NodeId = node.NodeId,
                        BlockType = node.BlockType,
                        StepStatus = finishStatus,
                        ExitCode = result.ExitCode,
                        OutputJson = outputJson,
                        Message = result.Succeeded
                            ? $"{node.DisplayName} completed (exit {result.ExitCode})"
                            : $"{node.DisplayName} failed (exit {result.ExitCode})",
                        Level = result.Succeeded ? LogLevelKind.Information : LogLevelKind.Error
                    }
                };

                events.AddRange(ToLineEvents(node, result.StdOut, LogLevelKind.Information));
                events.AddRange(ToLineEvents(node, result.StdErr, LogLevelKind.Error));

                await orchestratorClient.PostEventsAsync(credentials, payload.JobId, events, ct);

                if (!result.Succeeded)
                {
                    var summary = result.TimedOut
                        ? $"Step {node.DisplayName} timed out"
                        : $"Step {node.DisplayName} failed with exit code {result.ExitCode}";
                    await orchestratorClient.FailJobAsync(credentials, payload.JobId, summary, ct);
                    state.MarkJobFailed(payload.JobId, summary);
                    return;
                }
            }

            await orchestratorClient.CompleteJobAsync(credentials, payload.JobId, "Succeeded", ct);
            state.MarkJobSucceeded(payload.JobId);
            logger.LogInformation("Job {JobId} completed", payload.JobId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} failed", payload.JobId);
            state.MarkJobFailed(payload.JobId, ex.Message);
            try
            {
                await orchestratorClient.FailJobAsync(credentials, payload.JobId, ex.Message, CancellationToken.None);
            }
            catch (Exception failEx)
            {
                logger.LogWarning(failEx, "Failed to notify orchestrator of job failure");
            }
        }
    }

    private static IEnumerable<JobEventDto> ToLineEvents(NodeDto node, string text, LogLevelKind level)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            yield return new JobEventDto
            {
                NodeId = node.NodeId,
                BlockType = node.BlockType,
                Message = line,
                Level = level
            };
        }
    }

    private async Task TryDeleteCredentialsAsync(CancellationToken ct)
    {
        try
        {
            await credentialStore.DeleteAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to delete credential file");
        }
        state.ClearCredentials();
    }
}

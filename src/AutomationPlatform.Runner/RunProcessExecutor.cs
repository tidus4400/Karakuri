using System.Diagnostics;
using System.Text;
using AutomationPlatform.Shared;

namespace AutomationPlatform.Runner;

public sealed class RunProcessExecutor(ILogger<RunProcessExecutor> logger)
{
    private const int MaxCapturedChars = 64 * 1024;

    public async Task<RunProcessResult> ExecuteAsync(NodeDto node, CancellationToken cancellationToken)
    {
        var path = JsonHelpers.GetString(node.Config, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"RunProcess node '{node.DisplayName}' is missing config.path");
        }

        var args = JsonHelpers.GetString(node.Config, "args") ?? string.Empty;
        var workingDir = JsonHelpers.GetString(node.Config, "workingDir");
        var timeoutSec = JsonHelpers.GetInt(node.Config, "timeoutSec");
        var timeout = timeoutSec.HasValue && timeoutSec.Value > 0 ? TimeSpan.FromSeconds(timeoutSec.Value) : (TimeSpan?)null;

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDir))
        {
            startInfo.WorkingDirectory = workingDir;
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var sw = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process '{path}'.");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start process '{path}': {ex.Message}", ex);
        }

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, cancellationToken);
        var stderrTask = ReadBoundedAsync(process.StandardError, cancellationToken);

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout.HasValue)
        {
            waitCts.CancelAfter(timeout.Value);
        }

        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(waitCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        sw.Stop();

        var exitCode = timedOut ? -1 : process.ExitCode;
        var succeeded = !timedOut && exitCode == 0;

        logger.LogInformation(
            "RunProcess finished for node {NodeId} ({DisplayName}) with exitCode={ExitCode}, timedOut={TimedOut}, durationMs={DurationMs}",
            node.NodeId,
            node.DisplayName,
            exitCode,
            timedOut,
            sw.ElapsedMilliseconds);

        return new RunProcessResult
        {
            ExitCode = exitCode,
            TimedOut = timedOut,
            Succeeded = succeeded,
            StdOut = stdout.Text,
            StdErr = stderr.Text,
            StdOutTruncated = stdout.Truncated,
            StdErrTruncated = stderr.Truncated,
            DurationMs = sw.ElapsedMilliseconds
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private static async Task<BoundedText> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new char[1024];
        var truncated = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read <= 0) break;

            var remaining = MaxCapturedChars - sb.Length;
            if (remaining <= 0)
            {
                truncated = true;
                continue;
            }

            if (read > remaining)
            {
                sb.Append(buffer, 0, remaining);
                truncated = true;
            }
            else
            {
                sb.Append(buffer, 0, read);
            }
        }

        return new BoundedText(sb.ToString(), truncated);
    }

    private sealed record BoundedText(string Text, bool Truncated);
}

public sealed class RunProcessResult
{
    public int ExitCode { get; set; }
    public bool TimedOut { get; set; }
    public bool Succeeded { get; set; }
    public string StdOut { get; set; } = string.Empty;
    public string StdErr { get; set; } = string.Empty;
    public bool StdOutTruncated { get; set; }
    public bool StdErrTruncated { get; set; }
    public long DurationMs { get; set; }
}

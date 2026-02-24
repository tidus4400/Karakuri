using System.Runtime.InteropServices;
using AutomationPlatform.Runner;
using AutomationPlatform.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutomationPlatform.Runner.Tests;

public sealed class RunProcessExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Succeeds_AndCapturesStdOut()
    {
        var executor = new RunProcessExecutor(NullLogger<RunProcessExecutor>.Instance);
        var node = CreateRunProcessNode(ShellPath(), ShellEchoArgs("runner-ok"));

        var result = await executor.ExecuteAsync(node, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("runner-ok", result.StdOut);
    }

    [Fact]
    public async Task ExecuteAsync_TimesOut_AndKillsProcess()
    {
        var executor = new RunProcessExecutor(NullLogger<RunProcessExecutor>.Instance);
        var node = CreateRunProcessNode(ShellPath(), ShellSleepArgs(3), timeoutSec: 1);

        var result = await executor.ExecuteAsync(node, CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_BoundsStdOutCapture()
    {
        var executor = new RunProcessExecutor(NullLogger<RunProcessExecutor>.Instance);
        var payload = new string('x', 70000);
        var node = CreateRunProcessNode(ShellPath(), ShellEchoArgs(payload));

        var result = await executor.ExecuteAsync(node, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.StdOutTruncated);
        Assert.True(result.StdOut.Length <= 64 * 1024);
    }

    private static NodeDto CreateRunProcessNode(string path, string args, int? timeoutSec = null)
    {
        var config = new Dictionary<string, object?>
        {
            ["path"] = path,
            ["args"] = args,
            ["workingDir"] = "",
        };
        if (timeoutSec.HasValue)
        {
            config["timeoutSec"] = timeoutSec.Value;
        }

        return new NodeDto
        {
            NodeId = "n1",
            BlockType = "RunProcess",
            DisplayName = "Test",
            Config = config
        };
    }

    private static string ShellPath()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "/bin/sh";

    private static string ShellEchoArgs(string text)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"/c echo {text}";
        }

        return $"-c \"printf %s {text}\"";
    }

    private static string ShellSleepArgs(int seconds)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"/c ping -n {seconds + 2} 127.0.0.1 > nul";
        }

        return $"-c \"sleep {seconds}\"";
    }
}

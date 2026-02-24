using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AutomationPlatform.Runner;

public sealed class RunnerCredentialStore(IHostEnvironment env, IOptions<RunnerOptions> options)
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private string ResolvePath()
    {
        var configured = options.Value.CredentialFile;
        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        return Path.Combine(env.ContentRootPath, configured);
    }

    public async Task<RunnerCredentials?> LoadAsync(CancellationToken ct = default)
    {
        var path = ResolvePath();
        if (!File.Exists(path))
        {
            return null;
        }

        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RunnerCredentials>(fs, _json, ct);
    }

    public async Task SaveAsync(RunnerCredentials credentials, CancellationToken ct = default)
    {
        var path = ResolvePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, credentials, _json, ct);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public Task DeleteAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = ResolvePath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }
}

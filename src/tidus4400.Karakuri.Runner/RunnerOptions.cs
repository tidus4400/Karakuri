namespace tidus4400.Karakuri.Runner;

public sealed class RunnerOptions
{
    public string ServerUrl { get; set; } = "http://localhost:5010";
    public string RegistrationToken { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tags { get; set; } = "local";
    public int LongPollWaitSeconds { get; set; } = 45;
    public int LocalPort { get; set; } = 5180;
    public string CredentialFile { get; set; } = "runner.credentials.json";
    public int HeartbeatSeconds { get; set; } = 15;
}

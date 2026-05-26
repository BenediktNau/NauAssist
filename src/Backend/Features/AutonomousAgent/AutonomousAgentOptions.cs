namespace NauAssist.Backend.Features.AutonomousAgent;

public sealed class AutonomousAgentOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 20;
    public int InitialDelaySeconds { get; set; } = 30;
    public int PendingRetentionDays { get; set; } = 7;
}

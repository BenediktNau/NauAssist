namespace NauAssist.Backend.Features.AutonomousAgent.Sources;

public sealed record SourceAccount(
    long Id,
    string Kind,
    string DisplayName,
    string CredentialsJson,
    IReadOnlyList<string> Allowlist,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

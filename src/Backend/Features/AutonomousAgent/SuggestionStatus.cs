namespace NauAssist.Backend.Features.AutonomousAgent;

public enum SuggestionStatus
{
    Pending,
    Responded,
    Dismissed,
}

internal static class SuggestionStatusExtensions
{
    public static string ToWire(this SuggestionStatus s) => s switch
    {
        SuggestionStatus.Pending => "pending",
        SuggestionStatus.Responded => "responded",
        SuggestionStatus.Dismissed => "dismissed",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unbekannter SuggestionStatus."),
    };

    public static SuggestionStatus ParseWire(string s) => s switch
    {
        "pending" => SuggestionStatus.Pending,
        "responded" => SuggestionStatus.Responded,
        "dismissed" => SuggestionStatus.Dismissed,
        _ => throw new ArgumentException($"Unbekannter SuggestionStatus '{s}'.", nameof(s)),
    };
}

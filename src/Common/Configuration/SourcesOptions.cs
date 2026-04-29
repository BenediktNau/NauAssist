using System.Collections.Generic;

namespace NauAssist.Common.Configuration;

/// <summary>
/// Whitelist der Wahrnehmungs-Quellen (Prinzip v: Selektive Wahrnehmung).
/// Was hier nicht eingetragen ist, existiert für den Agenten nicht. Die
/// konkreten Verbindungstypen werden in späteren Etappen befüllt.
/// </summary>
public sealed class SourcesOptions
{
    public const string SectionName = "Sources";

    public IList<string> Mailboxes { get; set; } = new List<string>();

    public IList<string> CalendarEndpoints { get; set; } = new List<string>();

    public IList<string> ChatChannels { get; set; } = new List<string>();
}

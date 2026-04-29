using System.ComponentModel.DataAnnotations;

namespace NauAssist.Common.Configuration;

/// <summary>
/// Eingangs-Konfiguration für den <see cref="IPathResolver"/>. Pfade dürfen
/// relativ oder absolut sein; relative Pfade werden gegen
/// <see cref="BaseDirectory"/> aufgelöst (oder, falls nicht gesetzt, gegen
/// das aktuelle Arbeitsverzeichnis).
/// </summary>
public sealed class PathOptions
{
    public const string SectionName = "Paths";

    /// <summary>Anker für relative Pfade. Üblich: das Repo- bzw. Container-Wurzelverzeichnis.</summary>
    public string? BaseDirectory { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CoreRoot { get; set; } = "src";

    [Required(AllowEmptyStrings = false)]
    public string ExtensionsRoot { get; set; } = "extensions";

    [Required(AllowEmptyStrings = false)]
    public string DataRoot { get; set; } = "data";

    [Required(AllowEmptyStrings = false)]
    public string LogsRoot { get; set; } = "data/logs";

    [Required(AllowEmptyStrings = false)]
    public string ModelsRoot { get; set; } = "models";
}

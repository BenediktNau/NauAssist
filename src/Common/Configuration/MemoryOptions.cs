using System.ComponentModel.DataAnnotations;

namespace NauAssist.Common.Configuration;

public sealed class MemoryOptions
{
    public const string SectionName = "Memory";

    /// <summary>SQLite-Datei für Layer 1–3 (Regeln, Entitäten, Topics) und Layer 4 (FTS5). Relativ zu <see cref="IPathResolver.DataRoot"/>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string DatabaseFile { get; set; } = "memory.db";

    /// <summary>Aktiviert FTS5-basierten episodischen Recall (Layer 4) bis zur Vector-DB-Migration.</summary>
    public bool Layer4FtsEnabled { get; set; } = true;
}

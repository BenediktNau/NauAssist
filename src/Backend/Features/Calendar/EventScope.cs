namespace NauAssist.Backend.Features.Calendar;

/// <summary>
/// Anwendungsbereich für Lösch-/Update-Operationen auf Serien-Events.
/// <list type="bullet">
///   <item><description><c>Instance</c> — nur die konkrete Instanz (Standard, auch für Einzeltermine).</description></item>
///   <item><description><c>Series</c> — die gesamte Serie (Master). Greift nur, wenn das Ziel eine Serien-Instanz ist.</description></item>
/// </list>
/// "this and following" ist bewusst nicht abgebildet — siehe Diskussion mit User.
/// </summary>
public enum EventScope
{
    Instance = 0,
    Series = 1,
}

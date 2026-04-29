using System;
using System.ComponentModel.DataAnnotations;

namespace NauAssist.Common.Configuration;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    /// <summary>Reflexions-Loop-Intervall (Konzept §3, Etappe 5). Default: 5 Minuten.</summary>
    public TimeSpan ReflectionInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Beginn der harten Sperrzone (Prinzip iv: Stille als Standardzustand).</summary>
    [Required(AllowEmptyStrings = false)]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Format 'HH:mm' erwartet.")]
    public string QuietHoursStart { get; set; } = "22:00";

    /// <summary>Ende der harten Sperrzone.</summary>
    [Required(AllowEmptyStrings = false)]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Format 'HH:mm' erwartet.")]
    public string QuietHoursEnd { get; set; } = "07:00";
}

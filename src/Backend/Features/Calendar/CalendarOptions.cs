namespace NauAssist.Backend.Features.Calendar;

public sealed class CalendarOptions
{
    public string WorkingHoursStart { get; set; } = "09:00";
    public string WorkingHoursEnd { get; set; } = "18:00";
    public int DefaultDurationMinutes { get; set; } = 60;
    public int SearchHorizonDays { get; set; } = 14;
    public string GoogleCalendarId { get; set; } = "primary";
    public string GoogleCredentialsPath { get; set; } = "./data/google-credentials.json";
}

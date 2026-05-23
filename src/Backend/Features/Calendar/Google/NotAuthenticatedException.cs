namespace NauAssist.Backend.Features.Calendar.Google;

public sealed class NotAuthenticatedException : Exception
{
    public NotAuthenticatedException(string message) : base(message) { }
}

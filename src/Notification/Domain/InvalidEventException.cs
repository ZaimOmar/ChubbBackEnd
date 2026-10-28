namespace Chubb.Notification.Domain;

public sealed class InvalidEventException : Exception
{
    public InvalidEventException(string reason)
        : base(reason)
    {
    }

    public InvalidEventException(string reason, Exception innerException)
        : base(reason, innerException)
    {
    }
}

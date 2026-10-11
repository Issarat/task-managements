namespace TaskManager.Application.Exceptions;

public sealed class InvalidTaskColumnChangeException : Exception
{
    public InvalidTaskColumnChangeException()
        : base("A task can only move to an adjacent column.")
    {
    }
}

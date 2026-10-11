namespace API.Services;

/// <summary>
/// The target server could not be inspected or changed (SSH failure, unexpected script output).
/// </summary>
public class ServerSetupException : Exception
{
    public ServerSetupException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

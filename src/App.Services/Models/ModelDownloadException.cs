namespace App.Services.Models;

public sealed class ModelDownloadException : Exception
{
    public ModelDownloadException(string message)
        : base(message)
    {
    }

    public ModelDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

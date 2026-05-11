namespace App.Inference.Worker;

public sealed class TranscriptionWorkerException : Exception
{
    public TranscriptionWorkerException(string message)
        : base(message)
    {
    }

    public TranscriptionWorkerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

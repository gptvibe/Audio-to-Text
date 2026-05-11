namespace App.Core.Contracts;

public interface IDiagnosticInfoProvider
{
    Task<string> CreateDiagnosticInfoAsync(CancellationToken cancellationToken = default);
}

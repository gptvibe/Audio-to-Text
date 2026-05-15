using System.Diagnostics;
using App.Core.Contracts;
using App.Models.Domain;

namespace App.Inference.Worker;

internal sealed class LiveWorkerProcessSession : ILiveTranscriptionSession
{
    private readonly Process _process;
    private readonly IProgress<LiveTranscriptionEvent>? _progress;
    private readonly SemaphoreSlim _inputLock = new(1, 1);
    private readonly TaskCompletionSource _readyCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stoppedCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private bool _stopRequested;

    public LiveWorkerProcessSession(Process process, IProgress<LiveTranscriptionEvent>? progress)
    {
        _process = process;
        _progress = progress;
    }

    public Task Ready => _readyCompletion.Task;

    public bool IsReady => _readyCompletion.Task.IsCompletedSuccessfully;

    public void StartReading(CancellationToken cancellationToken)
    {
        _stdoutTask = ReadStdoutAsync(cancellationToken);
        _stderrTask = ReadStderrAsync(cancellationToken);
    }

    public void Cancel()
    {
        KillWorker();
    }

    public async Task SendAudioChunkAsync(LiveAudioChunk chunk, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Ready.WaitAsync(cancellationToken);

        var command = LiveWorkerProtocol.BuildChunkCommand(chunk);
        await _inputLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _process.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _inputLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_stopRequested)
        {
            await WaitForExitAsync(cancellationToken);
            return;
        }

        _stopRequested = true;
        if (!_process.HasExited)
        {
            await _inputLock.WaitAsync(cancellationToken);
            try
            {
                await _process.StandardInput.WriteLineAsync(LiveWorkerProtocol.BuildStopCommand());
                await _process.StandardInput.FlushAsync();
            }
            catch (InvalidOperationException)
            {
                // The worker may already have exited after a cancellation or error.
            }
            catch (IOException)
            {
                // The worker may already have exited after a cancellation or error.
            }
            finally
            {
                _inputLock.Release();
            }
        }

        await WaitForExitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }

        try
        {
            if (_stdoutTask is not null)
            {
                await _stdoutTask.ConfigureAwait(false);
            }
        }
        catch
        {
            // Reader failures are surfaced through events and ready/stop tasks.
        }

        try
        {
            if (_stderrTask is not null)
            {
                await _stderrTask.ConfigureAwait(false);
            }
        }
        catch
        {
            // Reader failures are surfaced through events and ready/stop tasks.
        }

        _inputLock.Dispose();
        _process.Dispose();
    }

    private async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAny(_stoppedCompletion.Task, _process.WaitForExitAsync(cancellationToken));
            await _process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillWorker();
            throw;
        }
    }

    private async Task ReadStdoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                LiveTranscriptionEvent liveEvent;
                try
                {
                    liveEvent = LiveWorkerProtocol.ParseLiveEvent(line);
                }
                catch (Exception ex)
                {
                    liveEvent = new LiveTranscriptionEvent
                    {
                        Kind = LiveTranscriptionEventKind.Error,
                        Message = $"The live worker returned invalid JSON. {ex.Message}"
                    };
                }

                _progress?.Report(liveEvent);

                if (liveEvent.Kind == LiveTranscriptionEventKind.Ready)
                {
                    _readyCompletion.TrySetResult();
                }
                else if (liveEvent.Kind == LiveTranscriptionEventKind.Error)
                {
                    var exception = new TranscriptionWorkerException(liveEvent.Message ?? "The live transcription worker failed.");
                    _readyCompletion.TrySetException(exception);
                }
                else if (liveEvent.Kind == LiveTranscriptionEventKind.Stopped)
                {
                    _stoppedCompletion.TrySetResult();
                }
            }

            if (!_readyCompletion.Task.IsCompleted)
            {
                _readyCompletion.TrySetException(new TranscriptionWorkerException("The live transcription worker exited before it was ready."));
            }

            _stoppedCompletion.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            _readyCompletion.TrySetCanceled(ex.CancellationToken);
            _stoppedCompletion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            _readyCompletion.TrySetException(ex);
            _stoppedCompletion.TrySetException(ex);
        }
    }

    private async Task ReadStderrAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stderr = await _process.StandardError.ReadToEndAsync(cancellationToken);
            if (_process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
            {
                var exception = new TranscriptionWorkerException(stderr.Trim());
                _readyCompletion.TrySetException(exception);
                _stoppedCompletion.TrySetException(exception);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is handled by the stdout loop and owner.
        }
    }

    private void KillWorker()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cancellation.
        }
    }
}

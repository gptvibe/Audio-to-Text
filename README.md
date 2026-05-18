# QuietScribe

A local-first Windows speech-to-text desktop app built with WinUI 3, C#/.NET, and a swappable Python inference worker.

The v1 path focuses on reliable local transcription:

- Modern WinUI 3 shell with a persistent local transcript sidebar, New Transcription, Live Transcription, Models, and Settings.
- Hugging Face model browsing, custom repo IDs, resumable downloads, validation, delete, and open-folder actions.
- Secure Hugging Face token storage through Windows Credential Manager.
- CPU/GPU/NPU detection that never blocks CPU fallback.
- Drag-and-drop or file-picker transcription for common audio/video formats.
- Background Python worker for `faster-whisper` transcription.
- Live Transcription mode with microphone selection, WASAPI capture, partial text updates, stable timestamped segments, pause/resume, copy, clear, and `.txt` export.
- Optional speaker-label mode when diarization dependencies and gated Hugging Face access are available.
- Transcript editor with search, copy, speaker renaming, local history loading, delete, and `.txt` export.
- Local app data, logs, settings, model cache, and diagnostics.

No AI chat features are included. Audio and transcripts stay on the device; the network is used only for model downloads.

## Requirements

- Windows 10 1809 or newer, Windows 11 recommended.
- .NET SDK with WinUI templates installed.
- Release builds include an embedded Python transcription runtime for core `faster-whisper` transcription.
- `ffmpeg` on `PATH` is optional. The bundled PyAV stack can decode many common files; ffmpeg can still help with edge-case media.

For development from source, install worker dependencies:

```powershell
cd workers/transcription-worker
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

Build release artifacts with the bundled runtime:

```powershell
.\scripts\build-release.ps1
```

Public releases are portable-only for now because unsigned setup installers can be blocked by Windows Smart App Control. The portable zip uses the standard .NET/WinUI publish layout so Windows security features see the same app executable that was built by the desktop project. Open `QuietScribe.exe` from the extracted folder.

## Build

```powershell
dotnet build QuietScribe.slnx
```

Run from source:

```powershell
dotnet run --project src/App.Desktop/App.Desktop.csproj -p:RuntimeIdentifier=win-x64
```

## Live Transcription

Open **Live Transcription**, choose a downloaded faster-whisper model, choose a microphone, and press **Start**. The app captures 16 kHz mono PCM audio locally, sends rolling WAV chunks to a persistent `worker.py live` process, and shows partial text while stable segments are committed into the transcript.

Tiny, base, and small faster-whisper models are recommended for live mode. Larger models can improve accuracy, but may lag unless CUDA or a fast CPU is available. Live speaker diarization is not required; existing speaker detection remains a file/post-processing workflow. See [docs/live-transcription.md](docs/live-transcription.md) for latency, hardware, and protocol notes.

## Project Structure

```text
/src
  /App.Desktop      WinUI 3 desktop UI
  /App.Core         Interfaces and formatting
  /App.Inference    Hardware detection and worker client
  /App.Models       Shared typed records/enums
  /App.Services     Settings, token storage, models, export, history
  /App.Tests        Focused unit tests
/workers
  /transcription-worker
/docs
  architecture.md
  setup.md
  model-support.md
  troubleshooting.md
```

More detail lives in [docs/setup.md](docs/setup.md), [docs/architecture.md](docs/architecture.md), [docs/model-support.md](docs/model-support.md), and [docs/troubleshooting.md](docs/troubleshooting.md).

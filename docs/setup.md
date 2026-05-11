# Setup

## Build the Windows app

```powershell
dotnet build QuietScribe.slnx
```

The desktop project defaults `AnyCPU` builds to `win-x64` because packaged WinUI apps cannot be processor-neutral.

Run:

```powershell
dotnet run --project src/App.Desktop/App.Desktop.csproj -p:RuntimeIdentifier=win-x64
```

## Python worker

```powershell
cd workers/transcription-worker
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

The app launches `python` from `PATH`. If you use a venv, start the app from a shell where that venv is active, or adjust `TranscriptionWorkerClient` to point at the venv Python executable.

## ffmpeg

`ffmpeg` is recommended for consistent audio extraction from video files:

```powershell
winget install Gyan.FFmpeg
```

If `ffmpeg` is missing, the worker still tries to use `faster-whisper`/PyAV decoding, but error messages will be clearer with `ffmpeg` installed.

## App data

Local data is stored under:

```text
%LOCALAPPDATA%\QuietScribe
```

Important folders:

- `models`: Hugging Face model cache.
- `history`: local transcript history JSON.
- `logs`: local diagnostic logs.
- `temp`: temporary audio conversion workspace.

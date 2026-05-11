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

## Release build with bundled worker

```powershell
.\scripts\build-release.ps1
```

This produces:

- `artifacts\release\QuietScribe-v0.1.2-win-x64-setup.exe`
- `artifacts\release\QuietScribe-v0.1.2-win-x64-portable.zip`

Both include an embedded Python runtime with the core `faster-whisper` stack installed. The app prefers `workers\transcription-worker\python\python.exe` automatically.

## Python worker for development

```powershell
cd workers/transcription-worker
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

`requirements-diarization.txt` is optional and significantly larger.

## ffmpeg

`ffmpeg` is optional but useful for edge-case media files:

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

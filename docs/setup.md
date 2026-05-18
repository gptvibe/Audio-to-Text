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

- `artifacts\release\QuietScribe-v0.2.1-win-x64-portable.zip`

The portable zip includes an embedded Python runtime with the core `faster-whisper` stack installed. It uses the standard .NET/WinUI publish layout to avoid the custom launcher that could trigger Smart App Control on some machines. Open `QuietScribe.exe` from the extracted folder. The app prefers `workers\transcription-worker\python\python.exe` automatically.

Unsigned setup installers can be blocked by Windows Smart App Control, so public releases are portable-only until QuietScribe has code signing. To build a local unsigned installer anyway, run:

```powershell
.\scripts\build-release.ps1 -BuildSetup
```

The setup installer always creates a Start Menu shortcut. The desktop shortcut is an optional checkbox in the installer and is off by default.

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

# Transcription Worker

This worker keeps ML dependencies out of the WinUI process. It speaks JSON Lines over stdout:

- `progress` events update the UI.
- `result` returns transcript segments.
- `error` gives a user-friendly failure message.

## Setup

```powershell
cd workers/transcription-worker
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

Release builds bundle the core `faster-whisper` runtime under `workers/transcription-worker/python`, so users do not need to install Python for basic transcription.

For development from source, `requirements.txt` installs the core transcription stack. `requirements-diarization.txt` is optional and only needed for speaker diarization. Some diarization models are gated on Hugging Face, so set `HF_TOKEN` or add a token in the desktop app when that flow is wired for your environment.

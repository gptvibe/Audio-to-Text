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

`faster-whisper` is required for v1 file transcription. `pyannote.audio` is optional and only needed for speaker diarization. Some diarization models are gated on Hugging Face, so set `HF_TOKEN` or add a token in the desktop app when that flow is wired for your environment.

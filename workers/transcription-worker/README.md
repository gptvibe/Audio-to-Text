# Transcription Worker

This worker keeps ML dependencies out of the WinUI process. It speaks JSON Lines over stdout:

- `progress` events update the UI.
- `result` returns transcript segments.
- `error` gives a user-friendly failure message.

File transcription uses `worker.py transcribe`, which starts, processes one file, emits a final result, and exits.

Live microphone transcription uses `worker.py live`. It loads the selected model once, reads JSON chunk commands from stdin, and emits:

- `ready`
- `live_partial`
- `live_segment`
- `live_progress`
- `live_error`
- `live_stopped`

Live chunks are temporary 16 kHz mono PCM WAV files created by the desktop app. The worker does not capture microphone audio directly.

## Setup

```powershell
cd workers/transcription-worker
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

Release builds bundle the core `faster-whisper` runtime under `workers/transcription-worker/python`, so users do not need to install Python for basic transcription.

For development from source, `requirements.txt` installs the core transcription stack. `requirements-diarization.txt` is optional and only needed for speaker diarization. Some diarization models are gated on Hugging Face, so set `HF_TOKEN` or add a token in the desktop app when that flow is wired for your environment.

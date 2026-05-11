# Architecture

QuietScribe is split into thin, replaceable layers:

- `App.Desktop`: WinUI 3 app shell and pages.
- `App.Models`: immutable records and enums shared across layers.
- `App.Core`: contracts plus transcript formatting.
- `App.Services`: app data paths, settings, Credential Manager token storage, Hugging Face downloads, history, export, and diagnostics.
- `App.Inference`: hardware detection and a JSON Lines client for the worker process.
- `workers/transcription-worker`: Python process that loads ML libraries and returns progress/results.

## Data Flow

1. The user chooses or downloads a model.
2. `HuggingFaceModelManager` stores it under `%LOCALAPPDATA%\QuietScribe\models`.
3. The user drops or selects a media file.
4. `TranscriptionWorkerClient` starts `worker.py` with the selected model path and options.
5. The worker emits JSONL `progress`, `result`, or `error` events.
6. The UI renders an editable transcript.
7. `HistoryService` stores transcript metadata locally.
8. `.txt` export writes the edited transcript text selected by the user.

## Worker Protocol

Progress event:

```json
{"event":"progress","stage":"Transcribing","message":"Transcribed 8 segments","percent":54}
```

Result event:

```json
{
  "event": "result",
  "language": "en",
  "duration": 123.4,
  "segments": [
    {"start": 0.0, "end": 3.2, "speaker": "Speaker 1", "text": "Hello."}
  ]
}
```

Errors are reported as:

```json
{"event":"error","message":"Helpful message for the UI"}
```

This keeps future backends swappable: OpenVINO, DirectML, CUDA-specific runners, or a native engine can implement the same contract.

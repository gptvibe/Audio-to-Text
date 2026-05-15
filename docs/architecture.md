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

## Live Transcription Flow

Live mode uses the same layered architecture with a longer-lived worker session:

1. The user chooses a downloaded model and microphone.
2. `App.Desktop` captures microphone audio through WASAPI and resamples it to 16 kHz mono PCM.
3. The desktop app keeps a bounded rolling buffer, skips obvious silence, and writes temporary WAV chunks.
4. `TranscriptionWorkerClient.StartLiveSessionAsync` starts `worker.py live`.
5. The worker loads the model once and emits `ready`.
6. The app sends JSONL `chunk` commands over stdin.
7. The worker emits `live_partial` for revisable text and `live_segment` for stable timestamped lines.
8. `LiveTranscriptMerger` removes duplicated overlap text and updates the editable transcript.
9. Stop sends a JSONL `stop` command; the worker emits `live_stopped`, then temporary chunks are cleaned up.

Live events:

```json
{"event":"ready","backend":"cuda","compute_type":"float16"}
{"event":"live_partial","chunk_id":4,"text":"current words","latency_ms":830}
{"event":"live_segment","chunk_id":4,"segment":{"start":2.1,"end":4.8,"text":"Stable words."}}
{"event":"live_progress","chunk_id":4,"message":"Live transcript updated","latency_ms":830}
{"event":"live_stopped","audio_position":24.0}
```

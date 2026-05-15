# Live Transcription

QuietScribe includes a local-first live microphone mode for near-real-time transcription. It does not send audio to a cloud service.

## How It Works

1. The WinUI app captures microphone audio through WASAPI.
2. Captured audio is resampled to 16 kHz mono PCM.
3. The app keeps a short rolling audio buffer and writes temporary WAV chunks.
4. `worker.py live` starts one persistent Python worker and loads the selected faster-whisper model once.
5. The app sends chunk paths to the worker over stdin as JSON Lines.
6. The worker emits JSON Lines events:
   - `ready`
   - `live_partial`
   - `live_segment`
   - `live_progress`
   - `live_error`
   - `live_stopped`
7. Partial text is shown as a current line. Stable segments are committed into the transcript with timestamps.

The existing `worker.py transcribe` command is unchanged and remains the file transcription path.

## Latency Expectations

Live mode targets partial updates every 1-3 seconds after the model is loaded. Actual latency depends on:

- Model size.
- CPU/GPU speed.
- CUDA availability and VRAM.
- Microphone driver behavior.
- Background system load.

Tiny and base models are usually the best live choices on CPU. Small is a good balance when the machine can keep up. Medium and large models may be accurate, but they can lag on CPU-only systems.

## Recommended Models

- `Systran/faster-whisper-tiny`: lowest latency, lowest accuracy.
- `Systran/faster-whisper-base`: fast CPU option with better accuracy than tiny.
- `Systran/faster-whisper-small`: recommended balance for many laptops.
- Larger models: better quality, but use them for live mode only when GPU or high CPU headroom is available.

## Hardware Notes

The worker prefers CUDA when available through faster-whisper and falls back to CPU int8. The architecture keeps live mode behind the same worker protocol so future OpenVINO, DirectML, or NPU-backed runners can implement the same event contract.

NPU support is not required for live mode today.

## Silence and Overlap

The desktop app checks recent PCM audio for speech-like energy before submitting chunks, so it avoids repeatedly transcribing silence. Each live chunk uses a rolling window with overlap to reduce clipped words at boundaries. The app and worker both remove repeated overlap text before committing stable transcript lines.

## Speaker Labels

Real-time diarization is not required and is not part of the live loop. Existing speaker detection remains available for file transcription. Live speaker detection can be added later as a post-processing pass over a saved recording without blocking live partial text.

## Temporary Files

Live chunks are written under the system temp folder and deleted after the worker reports that each chunk has been processed. The in-memory audio buffer is bounded to a few seconds so long sessions do not grow memory usage without limit.

## Known Limitations

- Partial text may revise or disappear before a stable segment is committed.
- Very slow models can fall behind real time.
- Background noise can still trigger transcription.
- Live speaker labels are not implemented.
- Export currently writes `.txt`.

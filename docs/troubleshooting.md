# Troubleshooting

## Model download says token required

Add a Hugging Face token in Settings, then confirm your Hugging Face account has accepted any gated model terms.

## Transcription says the runtime could not be started

Use the v0.1.1 or newer setup/portable release. It includes `workers\transcription-worker\python\python.exe` and the app uses that runtime automatically.

## faster-whisper is not installed

This means the release runtime is incomplete or you are running from source without installing worker dependencies. For source builds, run:

```powershell
cd workers/transcription-worker
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

## Unsupported or corrupted media

Install `ffmpeg` and retry. If the file is still rejected, convert it to `.wav` or `.mp3` and try again.

## GPU/NPU not detected

The app never requires acceleration. CPU fallback is expected. For NVIDIA acceleration, verify `nvidia-smi` works in PowerShell and install a CUDA-capable Python/PyTorch stack.

## Out of memory

Use a smaller model, choose CPU only or Low power mode, and close memory-heavy apps. Large v3 models can require several GB of RAM/VRAM.

## Logs and diagnostics

Open Settings and use:

- `Copy diagnostic info`
- `Open logs folder`

Logs are stored locally in:

```text
%LOCALAPPDATA%\QuietScribe\logs
```

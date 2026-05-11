# Troubleshooting

## Model download says token required

Add a Hugging Face token in Settings, then confirm your Hugging Face account has accepted any gated model terms.

## Transcription says Python could not be started

Install Python 3.10+ and make sure `python` is on `PATH`. If using a virtual environment, start the app from the activated environment or configure the worker client to use that venv executable.

## faster-whisper is not installed

Run:

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

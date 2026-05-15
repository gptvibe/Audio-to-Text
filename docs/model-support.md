# Model Support

The first supported backend is `faster-whisper`, which expects CTranslate2-compatible Whisper model folders.

Default catalog:

- `Systran/faster-whisper-tiny`
- `Systran/faster-whisper-base`
- `Systran/faster-whisper-small`
- `Systran/faster-whisper-medium`
- `Systran/faster-whisper-large-v3`
- `Systran/faster-distil-whisper-large-v3`
- `openai/whisper-small` is listed as a reference model, but the v1 worker prefers faster-whisper converted repos.

Advanced users can paste any Hugging Face repo ID on the Models page. The downloader:

- queries the Hugging Face model tree,
- skips Markdown files,
- resumes partially downloaded files using HTTP range requests when supported,
- writes a local manifest,
- validates local file sizes before marking the model ready,
- avoids redownloading complete models.

## Tokens and gated access

Some Hugging Face repos require account access. Add a token in Settings. The token is stored with Windows Credential Manager and is not written into settings JSON.

## Hardware acceleration

The app reports:

- CPU fallback,
- NVIDIA/CUDA candidates through `nvidia-smi`,
- AMD/Intel GPU candidates through Windows display adapter detection,
- Intel NPU candidates through Plug and Play device detection.

The release worker bundles Python plus the core `faster-whisper` stack and uses CPU or CUDA if the bundled/runtime libraries support it. OpenVINO, DirectML, and Intel NPU optimized execution are extension points.

## Diarization

Speaker detection is optional and is not bundled in the default release because pyannote/Torch is much larger and often requires gated Hugging Face access. The worker tries `pyannote.audio` when installed and when `HF_TOKEN` or `HUGGINGFACE_TOKEN` is present. If diarization is unavailable, the app still produces a transcript and labels everything as `Speaker 1` instead of failing transcription.

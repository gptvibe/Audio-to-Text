from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Any


SUPPORTED_EXTENSIONS = {
    ".mp3",
    ".wav",
    ".m4a",
    ".mp4",
    ".mov",
    ".webm",
    ".aac",
    ".flac",
}


def emit(payload: dict[str, Any]) -> None:
    print(json.dumps(payload, ensure_ascii=False), flush=True)


def progress(stage: str, message: str, percent: float | None = None) -> None:
    payload: dict[str, Any] = {"event": "progress", "stage": stage, "message": message}
    if percent is not None:
        payload["percent"] = percent
    emit(payload)


def fail(message: str, code: int = 1) -> None:
    emit({"event": "error", "message": message})
    raise SystemExit(code)


def resolve_compute(mode: str) -> tuple[str, str]:
    if mode == "CpuOnly":
        return "cpu", "int8"

    try:
        import torch

        if mode in {"Auto", "Fastest", "BestAccuracy"} and torch.cuda.is_available():
            return "cuda", "float16"
    except Exception:
        pass

    return "cpu", "int8"


def prepare_audio(input_path: Path) -> tuple[Path, tempfile.TemporaryDirectory[str] | None]:
    if input_path.suffix.lower() not in SUPPORTED_EXTENSIONS:
        fail(f"Unsupported file type: {input_path.suffix}. Use mp3, wav, m4a, mp4, mov, webm, aac, or flac.")

    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        # faster-whisper can decode many files through PyAV, so continue with a clear warning path.
        return input_path, None

    temp_dir = tempfile.TemporaryDirectory(prefix="quietscribe-")
    output_path = Path(temp_dir.name) / "prepared.wav"
    command = [
        ffmpeg,
        "-hide_banner",
        "-loglevel",
        "error",
        "-y",
        "-i",
        str(input_path),
        "-vn",
        "-ac",
        "1",
        "-ar",
        "16000",
        str(output_path),
    ]

    completed = subprocess.run(command, capture_output=True, text=True)
    if completed.returncode != 0:
        temp_dir.cleanup()
        detail = completed.stderr.strip() or "ffmpeg could not decode this file."
        fail(f"Audio preparation failed. {detail}")

    return output_path, temp_dir


def transcribe(args: argparse.Namespace) -> None:
    source = Path(args.input)
    model_path = Path(args.model)

    if not source.exists():
        fail("The selected audio or video file does not exist.")
    if not model_path.exists():
        fail("The selected model folder does not exist. Download or redownload the model first.")

    try:
        from faster_whisper import WhisperModel
    except Exception as exc:
        fail(f"faster-whisper is not installed in this Python environment. {exc}")

    progress("PreparingAudio", "Preparing audio", 8)
    prepared_audio, temp_dir = prepare_audio(source)

    try:
        device, compute_type = resolve_compute(args.mode)
        progress("LoadingModel", f"Loading model on {device}", 18)
        model = WhisperModel(str(model_path), device=device, compute_type=compute_type)

        task = "translate" if args.translate else "transcribe"
        progress("Transcribing", "Transcribing audio", 28)
        started_at = time.monotonic()
        segments_iter, info = model.transcribe(
            str(prepared_audio),
            language=args.language,
            task=task,
            vad_filter=True,
            word_timestamps=False,
        )

        segments: list[dict[str, Any]] = []
        duration = getattr(info, "duration", None)

        for index, segment in enumerate(segments_iter, start=1):
            item = {
                "start": float(segment.start),
                "end": float(segment.end),
                "text": segment.text.strip(),
            }
            segments.append(item)
            emit({"event": "segment", "segment": item})

            if duration and duration > 0:
                percent = 28 + min(58, (float(segment.end) / float(duration)) * 58)
            else:
                percent = min(86, 28 + index)
            progress("Transcribing", f"Transcribed {index} segment{'s' if index != 1 else ''}", percent)

        if args.diarize:
            progress("DetectingSpeakers", "Detecting speakers", 88)
            apply_diarization(prepared_audio, segments, args.speakers)

        progress("FinalizingTranscript", "Finalizing transcript", 96)
        detected_language = getattr(info, "language", None)
        if args.translate:
            detected_language = "en"

        emit(
            {
                "event": "result",
                "language": detected_language,
                "duration": duration,
                "elapsed": time.monotonic() - started_at,
                "segments": segments,
            }
        )
    finally:
        if temp_dir is not None:
            temp_dir.cleanup()


def apply_diarization(audio_path: Path, segments: list[dict[str, Any]], speakers: int | None) -> None:
    token = os.environ.get("HF_TOKEN") or os.environ.get("HUGGINGFACE_TOKEN")
    if not token:
        for segment in segments:
            segment["speaker"] = "Speaker 1"
        return

    try:
        from pyannote.audio import Pipeline
    except Exception:
        for segment in segments:
            segment["speaker"] = "Speaker 1"
        return

    try:
        pipeline = Pipeline.from_pretrained("pyannote/speaker-diarization-3.1", use_auth_token=token)
        kwargs: dict[str, Any] = {}
        if speakers:
            kwargs["num_speakers"] = speakers
        diarization = pipeline(str(audio_path), **kwargs)
    except Exception:
        for segment in segments:
            segment["speaker"] = "Speaker 1"
        return

    turns: list[tuple[float, float, str]] = []
    speaker_map: dict[str, str] = {}
    for turn, _, speaker_id in diarization.itertracks(yield_label=True):
        if speaker_id not in speaker_map:
            speaker_map[speaker_id] = f"Speaker {len(speaker_map) + 1}"
        turns.append((float(turn.start), float(turn.end), speaker_map[speaker_id]))

    for segment in segments:
        start = float(segment.get("start", 0))
        end = float(segment.get("end", start))
        best = "Speaker 1"
        best_overlap = 0.0
        for turn_start, turn_end, speaker in turns:
            overlap = max(0.0, min(end, turn_end) - max(start, turn_start))
            if overlap > best_overlap:
                best_overlap = overlap
                best = speaker
        segment["speaker"] = best


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="QuietScribe worker")
    subparsers = parser.add_subparsers(dest="command", required=True)

    transcribe_parser = subparsers.add_parser("transcribe")
    transcribe_parser.add_argument("--input", required=True)
    transcribe_parser.add_argument("--model", required=True)
    transcribe_parser.add_argument("--language")
    transcribe_parser.add_argument("--translate", action="store_true")
    transcribe_parser.add_argument("--diarize", action="store_true")
    transcribe_parser.add_argument("--speakers", type=int)
    transcribe_parser.add_argument("--mode", default="Auto")
    transcribe_parser.add_argument("--output-mode", default="PlainText")
    transcribe_parser.set_defaults(func=transcribe)
    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        fail("Transcription canceled.", code=130)

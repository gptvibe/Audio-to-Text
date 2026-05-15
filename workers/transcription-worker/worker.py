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


def live_error(message: str, chunk_id: int | None = None) -> None:
    payload: dict[str, Any] = {"event": "live_error", "message": message}
    if chunk_id is not None:
        payload["chunk_id"] = chunk_id
    emit(payload)


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


def split_words(text: str) -> list[str]:
    return [word.strip() for word in text.split() if word.strip()]


def normalize_word(word: str) -> str:
    punctuation = " \t\r\n.,;:!?\"'()[]"
    return word.strip(punctuation).lower()


def remove_overlap(committed_text: str, candidate_text: str, max_words: int = 18) -> str:
    candidate = candidate_text.strip()
    if not candidate:
        return ""
    if not committed_text.strip():
        return candidate

    committed_words = split_words(committed_text)[-max_words:]
    candidate_words = split_words(candidate)
    committed_normalized = [normalize_word(word) for word in committed_words if normalize_word(word)]
    candidate_normalized = [normalize_word(word) for word in candidate_words if normalize_word(word)]
    if not committed_normalized or not candidate_normalized:
        return candidate

    full_candidate = " ".join(candidate_normalized)
    committed_tail = " ".join(committed_normalized)
    if full_candidate in committed_tail:
        return ""

    overlap = min(len(committed_normalized), len(candidate_normalized))
    while overlap > 0:
        suffix = committed_normalized[-overlap:]
        prefix = candidate_normalized[:overlap]
        if suffix == prefix:
            return " ".join(candidate_words[overlap:]).strip()
        overlap -= 1

    return candidate


class LiveTranscriptState:
    def __init__(self, final_delay_seconds: float = 1.0) -> None:
        self.final_delay_seconds = final_delay_seconds
        self.committed_until = 0.0
        self.committed_text = ""
        self.partial_text = ""

    def apply(
        self,
        segments: list[dict[str, Any]],
        chunk_end: float,
        is_final: bool,
    ) -> tuple[list[dict[str, Any]], str]:
        final_cutoff = chunk_end if is_final else max(0.0, chunk_end - self.final_delay_seconds)
        finalized: list[dict[str, Any]] = []
        partial_candidates: list[str] = []

        for segment in segments:
            text = str(segment.get("text") or "").strip()
            if not text:
                continue

            end = float(segment.get("end") or 0.0)
            if end <= self.committed_until + 0.08:
                continue

            if end <= final_cutoff:
                deduped = remove_overlap(self.committed_text, text)
                if not deduped:
                    continue

                item = dict(segment)
                item["text"] = deduped
                finalized.append(item)
                self.committed_until = max(self.committed_until, end)
                self.committed_text = f"{self.committed_text} {deduped}".strip()
            else:
                partial_candidates.append(text)

        self.partial_text = remove_overlap(self.committed_text, " ".join(partial_candidates))
        return finalized, self.partial_text


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


def live(args: argparse.Namespace) -> None:
    model_path = Path(args.model)
    if not model_path.exists():
        fail("The selected model folder does not exist. Download or redownload the model first.")

    try:
        from faster_whisper import WhisperModel
    except Exception as exc:
        fail(f"faster-whisper is not installed in this Python environment. {exc}")

    device, compute_type = resolve_compute(args.mode)
    emit(
        {
            "event": "live_progress",
            "message": f"Loading live model on {device}",
            "backend": device,
            "compute_type": compute_type,
        }
    )

    model = WhisperModel(str(model_path), device=device, compute_type=compute_type)
    task = "translate" if args.translate else "transcribe"
    state = LiveTranscriptState(final_delay_seconds=args.final_delay)
    last_audio_position = 0.0

    emit(
        {
            "event": "ready",
            "message": "Live worker ready",
            "backend": device,
            "compute_type": compute_type,
        }
    )

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        try:
            command = json.loads(line)
        except json.JSONDecodeError as exc:
            live_error(f"Invalid live command JSON. {exc}")
            continue

        command_name = command.get("command")
        if command_name == "stop":
            emit(
                {
                    "event": "live_stopped",
                    "message": "Live transcription stopped",
                    "audio_position": last_audio_position,
                }
            )
            break

        if command_name != "chunk":
            live_error(f"Unsupported live command: {command_name}")
            continue

        chunk_id = int(command.get("id") or 0)
        chunk_path = Path(str(command.get("path") or ""))
        audio_start = float(command.get("start") or 0.0)
        duration = float(command.get("duration") or 0.0)
        is_final = bool(command.get("is_final") or False)
        chunk_end = audio_start + duration
        last_audio_position = max(last_audio_position, chunk_end)

        if not chunk_path.exists():
            live_error("The live audio chunk could not be found.", chunk_id)
            continue

        started_at = time.monotonic()
        emit(
            {
                "event": "live_progress",
                "chunk_id": chunk_id,
                "message": "Transcribing live audio",
                "audio_position": chunk_end,
            }
        )

        try:
            segments_iter, _ = model.transcribe(
                str(chunk_path),
                language=args.language,
                task=task,
                vad_filter=True,
                word_timestamps=False,
                beam_size=1,
                best_of=1,
                condition_on_previous_text=False,
            )

            absolute_segments: list[dict[str, Any]] = []
            for segment in segments_iter:
                text = segment.text.strip()
                if not text:
                    continue
                absolute_segments.append(
                    {
                        "start": audio_start + float(segment.start),
                        "end": audio_start + float(segment.end),
                        "text": text,
                    }
                )

            finalized, partial_text = state.apply(absolute_segments, chunk_end, is_final)
            latency_ms = (time.monotonic() - started_at) * 1000

            for segment in finalized:
                emit(
                    {
                        "event": "live_segment",
                        "chunk_id": chunk_id,
                        "segment": segment,
                        "audio_position": chunk_end,
                        "latency_ms": latency_ms,
                    }
                )

            emit(
                {
                    "event": "live_partial",
                    "chunk_id": chunk_id,
                    "text": partial_text,
                    "audio_position": chunk_end,
                    "latency_ms": latency_ms,
                }
            )

            message = "Listening for speech" if not absolute_segments else "Live transcript updated"
            emit(
                {
                    "event": "live_progress",
                    "chunk_id": chunk_id,
                    "message": message,
                    "audio_position": chunk_end,
                    "latency_ms": latency_ms,
                    "backend": device,
                    "compute_type": compute_type,
                }
            )
        except Exception as exc:
            live_error(f"Live transcription failed for chunk {chunk_id}. {exc}", chunk_id)

    else:
        emit(
            {
                "event": "live_stopped",
                "message": "Live transcription input closed",
                "audio_position": last_audio_position,
            }
        )


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

    live_parser = subparsers.add_parser("live")
    live_parser.add_argument("--model", required=True)
    live_parser.add_argument("--language")
    live_parser.add_argument("--translate", action="store_true")
    live_parser.add_argument("--mode", default="Auto")
    live_parser.add_argument("--final-delay", type=float, default=1.0)
    live_parser.set_defaults(func=live)
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

"""Local STT worker. One JSON line per input file; stdout never contains logs."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

MODELS = ("tiny", "base", "small", "medium", "large-v3", "large-v3-turbo")
DOUBTFUL = 0.5  # word probability below which the app asks to check the word


def main() -> int:
    parser = argparse.ArgumentParser(description="Transcribe WAV clips locally with faster-whisper")
    parser.add_argument("--file", action="append", required=True, help="WAV path; repeat for a batch")
    parser.add_argument("--model", choices=MODELS, default="small")
    parser.add_argument("--device", choices=("auto", "cpu", "cuda"), default="auto")
    parser.add_argument("--language", choices=("es", "auto", "en", "ja"), default="es")
    parser.add_argument("--compute", choices=("auto", "float16", "int8_float16", "int8"), default="auto",
                        help="auto: float16 on GPU (int8_float16 if the GPU lacks float16), int8 on CPU")
    parser.add_argument("--hint", default="", help="Names and words to expect (initial prompt), e.g. character names")
    parser.add_argument("--words", action="store_true", help="Return word timestamps and probabilities")
    args = parser.parse_args()

    try:
        from faster_whisper import WhisperModel
    except ImportError as exc:
        print("Falta faster-whisper. Ejecuta scripts\\stt-setup.ps1 para instalarlo en worker\\.venv.", file=sys.stderr)
        print(str(exc), file=sys.stderr)
        return 2
    patch_av_open()

    def load(device: str):
        compute = args.compute if args.compute != "auto" else ("float16" if device == "cuda" else "int8")
        print(f"Cargando modelo {args.model} en {device} ({compute})…", file=sys.stderr, flush=True)
        try:
            return WhisperModel(args.model, device=device, compute_type=compute), compute
        except ValueError as exc:
            # Older GPUs (e.g. GTX 10xx) have no efficient float16: int8 weights with float16 math.
            if device == "cuda" and compute == "float16" and "float16" in str(exc).lower():
                print(f"float16 no disponible ({exc}); usando int8_float16.", file=sys.stderr, flush=True)
                return WhisperModel(args.model, device=device, compute_type="int8_float16"), "int8_float16"
            raise

    hint = " ".join(args.hint.split())[:600] or None

    try:
        selected_device = "cuda" if args.device in ("auto", "cuda") else "cpu"
        try:
            model, compute = load(selected_device)
        except Exception as exc:
            if args.device != "auto" or not cuda_error(exc):
                raise
            print(f"CUDA no disponible ({exc}); continuando en CPU.", file=sys.stderr, flush=True)
            selected_device = "cpu"
            model, compute = load("cpu")

        for index, raw_path in enumerate(args.file):
            path = Path(raw_path)
            try:
                if not path.is_file():
                    raise FileNotFoundError(f"No existe el audio: {path.name}")

                def transcribe(current_model):
                    segments, info = current_model.transcribe(
                        str(path), language=None if args.language == "auto" else args.language,
                        task="transcribe", beam_size=5, vad_filter=False,
                        condition_on_previous_text=False, initial_prompt=hint,
                        word_timestamps=args.words,
                    )
                    texts, words = [], []
                    for segment in segments:  # segments is a generator: decoding happens here
                        if segment.text.strip():
                            texts.append(segment.text.strip())
                        for word in (segment.words or []) if args.words else []:
                            if word.word.strip():
                                words.append({"word": word.word.strip(), "start": round(float(word.start), 3),
                                              "end": round(float(word.end), 3), "probability": round(float(word.probability), 3)})
                    return " ".join(texts), info, words

                try:
                    transcription, info, words = transcribe(model)
                except Exception as exc:
                    if args.device != "auto" or selected_device == "cpu" or not cuda_error(exc):
                        raise
                    print(f"CUDA falló durante {path.name} ({exc}); continuando en CPU.", file=sys.stderr, flush=True)
                    selected_device = "cpu"
                    model, compute = load("cpu")
                    transcription, info, words = transcribe(model)

                text = transcription.strip()
                result = {
                    "index": index, "text": text,
                    "language": info.language, "device": selected_device, "compute": compute,
                    "error": None if text else "No se detectó voz; escucha el WAV y escribe el texto manualmente.",
                    "words": words or None,
                    "speechStart": words[0]["start"] if words else None,
                    "speechEnd": words[-1]["end"] if words else None,
                }
            except Exception as exc:
                result = {"index": index, "text": "", "language": None, "device": selected_device,
                          "error": f"{type(exc).__name__}: {exc}"}
            print(json.dumps(result, ensure_ascii=False), flush=True)
    except Exception as exc:
        print(f"No se pudo iniciar STT local: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 1
    return 0


def patch_av_open() -> None:
    """faster-whisper 1.2 opens audio with av.open(..., metadata_errors="ignore"); PyAV 19 dropped that argument.
    If the installed PyAV rejects it, retry without it instead of failing every file."""
    try:
        import av
    except ImportError:
        return
    original = av.open
    if getattr(original, "_loquendo_patched", False):
        return

    def open_compat(*args, **kwargs):
        try:
            return original(*args, **kwargs)
        except TypeError as exc:
            if "metadata_errors" not in kwargs or "metadata_errors" not in str(exc):
                raise
            kwargs.pop("metadata_errors")
            return original(*args, **kwargs)

    open_compat._loquendo_patched = True
    av.open = open_compat


def cuda_error(exc: Exception) -> bool:
    message = str(exc).lower()
    return any(part in message for part in ("cuda", "cudnn", "cublas", "gpu", "cudart"))


if __name__ == "__main__":
    raise SystemExit(main())

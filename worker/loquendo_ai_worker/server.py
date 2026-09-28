from __future__ import annotations

import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

HOST = "127.0.0.1"
PORT = 8765
MAX_BODY = 1024 * 1024

_IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp", ".bmp"}
_AUDIO_EXTENSIONS = {".wav", ".mp3", ".ogg", ".flac", ".m4a"}
_VIDEO_EXTENSIONS = {".mp4", ".mkv", ".avi", ".mov", ".webm", ".mpg", ".mpeg"}


def classify_stub(file_name: str) -> dict[str, Any]:
    """Placeholder deterministic analyzer. No file is opened in v0.0.2."""
    suffix = Path(file_name).suffix.lower()
    stem = Path(file_name).stem.lower()

    if suffix in _VIDEO_EXTENSIONS:
        kind = "Video"
    elif suffix in _AUDIO_EXTENSIONS:
        kind = "SoundEffect"
    elif suffix in _IMAGE_EXTENSIONS:
        kind = "CharacterSprite" if any(k in stem for k in ("sprite", "render", "char")) else "Background"
    else:
        kind = "Unknown"

    return {
        "kind": kind,
        "tags": {
            "source": "stub-v0.0.2",
            "extension": suffix or "none",
        },
    }


class Handler(BaseHTTPRequestHandler):
    server_version = "LoquendoAIWorker/0.0.1"

    def _json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:  # noqa: N802
        if self.path == "/health":
            self._json(200, {"ok": True, "worker": "loquendo-ai", "version": "0.0.1"})
            return
        self._json(404, {"error": "not_found"})

    def do_POST(self) -> None:  # noqa: N802
        if self.path != "/v1/assets/analyze":
            self._json(404, {"error": "not_found"})
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            self._json(400, {"error": "invalid_content_length"})
            return

        if length <= 0 or length > MAX_BODY:
            self._json(413, {"error": "invalid_body_size"})
            return

        try:
            payload = json.loads(self.rfile.read(length))
            file_name = str(payload["fileName"])
        except (json.JSONDecodeError, KeyError, TypeError, ValueError):
            self._json(400, {"error": "invalid_json"})
            return

        self._json(200, classify_stub(file_name))

    def log_message(self, fmt: str, *args: object) -> None:
        print(f"[{self.log_date_time_string()}] {fmt % args}")


if __name__ == "__main__":
    print(f"Loquendo AI Worker v0.0.2 -> http://{HOST}:{PORT}")
    ThreadingHTTPServer((HOST, PORT), Handler).serve_forever()

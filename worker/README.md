# Loquendo AI Worker

Worker local desacoplado del proceso principal. En v0.0.2 **no usa IA**: sólo ofrece el contrato HTTP que después alojará visión, embeddings, STT y modelos locales.

El STT local de hotfix 23 vive en `stt/transcribe.py`, independiente del servidor
HTTP. La app usa `worker\python\python.exe` (Python embebido que trae la versión
publicada desde v1.4.0) o, si no existe, `worker\.venv\Scripts\python.exe`. Si no hay
ninguno la app ofrece instalarlo; a mano: `scripts\stt-setup.ps1` (usa el Python del
equipo o descarga el embebido). Recibe varias rutas WAV
mediante `--file`, carga una vez el modelo faster-whisper y devuelve una línea
JSON por toma (`index`, `text`, `language`, `device`, `error`). Los errores
diagnósticos se escriben exclusivamente en stderr.

```powershell
python .\worker\loquendo_ai_worker\server.py
```

Endpoints iniciales:

- `GET /health`
- `POST /v1/assets/analyze` con `{ "fileName": "carlos_angry.png" }`

El servidor sólo escucha en `127.0.0.1` y en esta versión no recibe ni abre rutas arbitrarias.

# v0.0.4-alpha

- Schema de proyecto actualizado a v4.
- Nueva tabla `scene_script_blocks` y migración desde `dialogue_lines` experimental.
- Nueva pestaña Guion con CRUD de episodios y escenas.
- Editor de bloques estructurados y reordenamiento.
- Resolución de voz: override de perfil → perfil base del personaje.
- Overrides por diálogo de pitch, speed y volume.
- Generación secuencial de voces por escena con TTS7/SAPI5/SAPI4 según el perfil.
- Caché SHA-256 y reutilización de WAV.
- Duración WAV real y offsets temporales básicos.
- Consulta perezosa de assets por clase para evitar cargar bibliotecas masivas en la UI del guion.

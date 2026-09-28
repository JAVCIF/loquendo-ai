# Guion estructurado — v0.0.4-alpha

Esta versión convierte Loquendo AI de un catálogo/TTS en el primer esqueleto real de un creador de escenas.

## Qué incluye

- pestaña **Guion** con Episodios → Escenas → Bloques;
- schema v4 con `scene_script_blocks`;
- bloques: Diálogo, Narración, Fondo, Mostrar/Ocultar personaje, Imagen, SFX, Música, Video, Pausa, Texto, Transición y Comentario;
- personaje y perfil de voz por línea;
- overrides opcionales de pitch, velocidad y volumen;
- pausa por bloque;
- orden manual con subir/bajar;
- generación de todas las voces de una escena;
- caché determinista por SHA-256 de provider + voz + parámetros + texto;
- reutilización de WAV si la línea no cambió;
- duración leída del WAV real;
- `start_offset_ms` básico para formar el primer timeline lógico;
- WAV organizados en `generated/voices/episode_NNN/scene_NNN/`;
- reproducción del audio generado de una línea;
- migración automática schema v3 → v4.

## Timeline de esta alpha

El reloj avanza secuencialmente con diálogos/narraciones y pausas. Los bloques visuales, SFX, música y video quedan anclados al tiempo lógico actual, pero todavía no se mezclan ni renderizan. Esa composición llega en la siguiente fase.

## Caché

Una línea se considera igual si coinciden:

`provider + voice + sampleRate + pitch + speed + volume + text`

Si el hash y el WAV siguen presentes, **Generar voces de escena** reutiliza el archivo.

## Rendimiento con bibliotecas grandes

La pestaña Guion no carga las decenas de miles de assets en un ComboBox al abrir el proyecto. Los assets se consultan por tipo únicamente cuando un bloque los necesita, y luego se cachean en memoria.

## Siguiente fase

Compositor/timeline determinista: convertir los bloques estructurados en eventos temporales de audio/imagen y generar una primera preview automática antes de exportar a VEGAS.

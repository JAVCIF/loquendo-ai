# Voice Lab — v0.0.3-beta.1

Esta fase conecta la UI WPF con el bridge TTS x86 ya validado.

## Incluye

- Nueva pestaña **Voice Lab** por proyecto.
- Personajes persistentes en SQLite.
- Perfiles de voz persistentes y reutilizables.
- Asignación de un perfil base a cada personaje.
- Providers iniciales:
  - `loquendo7-native` — preferido para las voces que existen en TTS7.
  - `sapi5-x86` — catálogo complementario (Infovox/IVONA/ScanSoft/etc.).
- Enumeración real de voces desde el bridge x86.
- Preview WAV dentro de `cache/tts-preview` y reproducción desde la aplicación.
- Pitch base por provider:
  - TTS7: escala nativa 0..100, centro 50.
  - SAPI5: escala -10..10 mediante SAPI XML; la implementación final depende de cada motor/voz SAPI.
- Velocidad base opcional:
  - si `Fijar` está desmarcado, el motor usa el valor por defecto;
  - si está marcado, se guarda la escala propia del provider.
- Volumen y sample rate por perfil.
- Schema SQLite v3 y migración automática desde v1/v2.
- Columnas preparadas para futuros overrides por diálogo: pitch, speed y volume.

## Preparación

El bridge x86 debe estar publicado una vez:

```cmd
scripts\tts-publish.cmd
```

Luego:

```cmd
dotnet build .\LoquendoAI.sln
dotnet run --project .\src\LoquendoAI.App\LoquendoAI.App.csproj
```

O usa:

```cmd
scripts\run-voice-lab.cmd
```

## Flujo

1. Crea/abre un proyecto.
2. Abre **Voice Lab**.
3. Crea un personaje (ej. `Carlos`).
4. Pulsa **Nuevo** perfil.
5. Elige TTS7 o SAPI5, voz y pitch.
6. Deja velocidad en `Auto` si no quieres fijarla para ese personaje.
7. Pulsa **Escuchar**.
8. Guarda el perfil.
9. Selecciona el personaje y pulsa **Asignar perfil seleccionado**.

Un futuro editor de guion podrá heredar estos valores y aplicar overrides sólo en líneas concretas sin modificar la identidad base del personaje.

# Loquendo AI v0.0.3-alpha — TTS Bridge / Doctor

Esta alpha NO es todavía el Voice Lab. Su objetivo es inventariar y probar el ecosistema TTS Win32 antes de conectarlo a la UI.

## Nuevo proyecto

`src/LoquendoAI.TtsBridge32`

Se compila deliberadamente como **x86** (`win-x86`) porque las instalaciones clásicas de Loquendo/Infovox/Balabolka son de 32 bits.

## Comandos

```powershell
dotnet run --project .\src\LoquendoAI.TtsBridge32 -- scan

dotnet run --project .\src\LoquendoAI.TtsBridge32 -- voices --provider sapi5-x86
dotnet run --project .\src\LoquendoAI.TtsBridge32 -- voices --provider loquendo7-native
dotnet run --project .\src\LoquendoAI.TtsBridge32 -- voices --provider balcon

dotnet run --project .\src\LoquendoAI.TtsBridge32 -- synth --provider sapi5-x86 --voice "Jorge" --text "Hola amigos de YouTube" --out .\jorge_sapi.wav

dotnet run --project .\src\LoquendoAI.TtsBridge32 -- synth --provider loquendo7-native --voice "Jorge" --text "Hola amigos de YouTube" --out .\jorge_native.wav
```

Si Loquendo no se detecta por registro:

```powershell
... --engine "C:\ruta\a\Loquendo\LTTS7"
```

o directamente `--engine "...\LoqTTS7.dll"`.

## Proveedores/probes incluidos

- `sapi5-x86`: enumeración y síntesis WAV mediante COM SAPI5 de 32 bits.
- `loquendo7-native`: acceso directo experimental a `LoqTTS7.dll`, basado en las funciones documentadas por el proyecto `jojje/win32-loquendo`.
- `balcon`: utiliza la utilidad de consola oficial de Balabolka si `balcon.exe` está instalada.
- Balabolka GUI: se inventaría, pero no se automatiza por clicks.
- Infovox SpeechPad 2.2: se inventaría y se comprueba la presencia de `acatts.dll` / `AcaTtsSapi5.dll`; por ahora se prioriza SAPI5 para las voces Infovox.
- TextAloud: detección inicial; adapter pendiente hasta inspeccionar la instalación/CLI concreta.

## Protocolo para LoquendoAI.exe

`serve` implementa JSON Lines por stdin/stdout. Así el futuro GUI x64 podrá mantener un proceso x86 aislado y reiniciarlo si un motor viejo se cuelga.

Ejemplo de petición:

```json
{"command":"voices","provider":"sapi5-x86"}
```

Respuesta:

```json
{"ok":true,"error":null,"data":["Jorge","Carlos"]}
```

## Prueba recomendada

1. `scan`
2. `voices --provider sapi5-x86`
3. `voices --provider loquendo7-native`
4. sintetizar la misma frase con SAPI y Loquendo Native.
5. Si `balcon.exe` existe, repetir con `balcon`.

No hace falta crear un proyecto Loquendo AI para estas pruebas.

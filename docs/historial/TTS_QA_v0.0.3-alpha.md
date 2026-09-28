# TTS QA — v0.0.3-alpha hotfix 6

Esta ronda valida el bridge antes de conectarlo al Voice Lab.

## 0. Publicar el bridge x86

No requiere PowerShell:

```cmd
scripts\tts-publish.cmd
```

## 1. Suite de compatibilidad de texto (TTS7)

```cmd
scripts\tts-qa-loquendo.cmd
```

Genera 10 WAV en `tts-test-results\qa-loquendo` y `qa_report.csv`.
Prueba texto básico, acentos/ñ, signos, números, símbolos, comillas, saltos de línea,
texto largo y español extendido. Los WAV deben escucharse manualmente: la validación
automática sólo comprueba estructura RIFF/WAVE, tamaño, sample rate, canales y duración.

## 2. Controles TTS7

```cmd
scripts\tts-controls-loquendo.cmd
```

Genera la misma frase con velocidades nativas 30, 50 y 70. TTS7 usa `ttsSetSpeed`.
Pitch y volumen nativos quedan deliberadamente fuera hasta mapear sus parámetros sin adivinar.

## 3. Stress test

Primero:

```cmd
scripts\tts-stress-100.cmd
```

Si termina 100/100 sin fallos:

```cmd
scripts\tts-stress-500.cmd
```

Y finalmente:

```cmd
scripts\tts-stress-1000.cmd
```

Cada iteración usa el camino de producción seguro: bridge x86 -> worker nativo aislado ->
LoqTTS7.dll -> WAV -> verificación. Se reutiliza un WAV temporal para no dejar miles de
archivos y se conservan sólo muestras de control. Cada corrida crea un CSV.

## 4. SAPI

```cmd
scripts\tts-qa-sapi.cmd
scripts\tts-controls-sapi.cmd
```

Para una voz sólo-SAPI puede usarse directamente:

```cmd
artifacts\ttsbridge-win-x86\LoquendoAI.TtsBridge32.exe qa-suite --provider sapi5-x86 --voice "Juan" --out-dir .\tts-test-results\qa-sapi-juan
```

## Comandos generales

```text
qa-suite --provider <provider> --voice <voice> --out-dir <dir>
stress --provider <provider> --voice <voice> --count N --out-dir <dir>
controls-test --provider <provider> --voice <voice> --out-dir <dir>
```

Providers QA actuales: `loquendo7-native`, `sapi5-x86`.

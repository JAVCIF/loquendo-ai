# Análisis estático de los ejecutables proporcionados

## Balabolka

- Archivo examinado: `balabolka.exe`
- PE32 / Intel i386 / Win32 GUI.
- Empaquetado con UPX.
- FileVersion: **2.10.0.580**.
- ProductVersion: **2.10**.
- Copyright: 2006–2015 Ilya Morozov.
- El ejecutable GUI no se usará como backend primario. Loquendo AI prefiere SAPI5 directo o `balcon.exe` cuando esté disponible.

## SpeechPad / Infovox Desktop

- Archivo examinado: `SpeechPad.exe`
- PE32 / Intel i386 / Win32 GUI.
- FileVersion: **2.2.29140.0**.
- ProductVersion: **2.2.2220.0**.
- Contiene referencias a `acatts.dll` y a las claves de registro de `Acapela Group\Infovox Desktop\HW2L`.
- Es un frontend de Infovox/Acapela. No se observó una CLI específica de síntesis; sí aparecen switches MFC genéricos como Automation/Embedding/DDE.
- Por tanto, no merece la pena automatizar la UI: las rutas útiles son SAPI5 de 32 bits (`AcaTtsSapi5.dll`) o, más adelante, AcaTTS nativo (`acatts.dll`).

No se ejecutaron los binarios proporcionados; el análisis fue estático.

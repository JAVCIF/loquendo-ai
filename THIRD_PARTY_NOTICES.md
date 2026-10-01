# Software de terceros

Loquendo AI se publica bajo la [licencia MIT](LICENSE). Este documento resume el software de terceros que **se
incluye** en los paquetes publicados (instalador y portable), el que **se descarga** al usarlo y el que **no se
incluye** y cada usuario instala por su cuenta. Las licencias completas de lo incluido viajan con el paquete, en las
rutas indicadas.

> Esta lista es informativa. Si falta algo o hay un error, abre un issue: corregirlo tiene prioridad.

## Incluido en el instalador y en la portable

| Componente | Para qué | Licencia | Dónde | Proyecto |
| --- | --- | --- | --- | --- |
| .NET 10 runtime y WPF | Ejecutar la app (va dentro de `LoquendoAI.exe` y de `tts-bridge\`) | MIT | dentro de los ejecutables | <https://github.com/dotnet/runtime> · <https://github.com/dotnet/wpf> |
| Microsoft.Data.Sqlite | Base de datos del proyecto | MIT | dentro de `LoquendoAI.exe` | <https://github.com/dotnet/efcore> |
| SQLitePCLRaw | Enlace nativo de SQLite | Apache 2.0 | dentro de `LoquendoAI.exe` | <https://github.com/ericsink/SQLitePCL.raw> |
| SQLite | Motor de base de datos | Dominio público | dentro de `LoquendoAI.exe` | <https://sqlite.org/copyright.html> |
| Python 3.12 (distribución embebida) | Ejecutar la transcripción local | PSF License | `worker\python\` (`LICENSE.txt`) | <https://www.python.org> |
| pip | Instalar y reparar la transcripción | MIT | `worker\python\Lib\site-packages\pip*` | <https://pip.pypa.io> |
| faster-whisper | Transcripción local de voces grabadas | MIT | `worker\python\Lib\site-packages\` | <https://github.com/SYSTRAN/faster-whisper> |
| CTranslate2 | Motor de inferencia de faster-whisper | MIT | ídem | <https://github.com/OpenNMT/CTranslate2> |
| ONNX Runtime | Detección de voz (VAD) de faster-whisper | MIT | ídem | <https://github.com/microsoft/onnxruntime> |
| PyAV | Lectura de audio | BSD-3-Clause (incluye bibliotecas de FFmpeg bajo LGPL) | ídem | <https://github.com/PyAV-Org/PyAV> |
| tokenizers, huggingface_hub | Tokenizador y descarga de modelos | Apache 2.0 | ídem | <https://github.com/huggingface> |
| NumPy | Cálculo numérico | BSD-3-Clause | ídem | <https://numpy.org> |
| tqdm, PyYAML, requests, filelock, fsspec, packaging y demás dependencias de los anteriores | Utilidades | MIT / BSD / Apache 2.0 / MPL 2.0 según el paquete | ídem | cada paquete en PyPI |
| Microsoft Visual C++ Runtime (`msvcp140*.dll`, `vcruntime140*.dll`, `concrt140.dll`) | Lo necesitan CTranslate2 y ONNX Runtime | Licencia de redistribución de Microsoft Visual C++ | `worker\python\` | <https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist> |
| FFmpeg (compilación GPL de BtbN, con libx264) | Preview MP4, exportación a VEGAS y análisis de medios | **GPL v3** | `tools\ffmpeg\` (`LICENSE.txt`, `README.txt`) | <https://ffmpeg.org> · compilación: <https://github.com/BtbN/FFmpeg-Builds> |

Cada paquete de Python conserva su licencia en `worker\python\Lib\site-packages\<paquete>.dist-info\`.

**FFmpeg y la GPL.** Loquendo AI no enlaza FFmpeg: lo ejecuta como un programa aparte, así que su licencia GPL no se
extiende al código de Loquendo AI, que sigue siendo MIT. Los binarios se redistribuyen sin cambios: una versión
estable fijada (serie 8.1 por defecto) de una compilación fechada de BtbN. `tools\ffmpeg\README.txt` indica la
versión exacta, el commit de FFmpeg y la compilación de la que sale. El código fuente correspondiente está en
<https://git.ffmpeg.org/ffmpeg.git> (ese commit) y las recetas y bibliotecas de la compilación en
<https://github.com/BtbN/FFmpeg-Builds>. Si alguno deja de estar disponible, pídelo en un issue y se te facilitará. Si prefieres otro FFmpeg, publica con `scripts\publicar.ps1 -SinFFmpeg` y
pon el tuyo en el PATH.

## Se descarga al usarlo

| Componente | Cuándo | Licencia | Proyecto |
| --- | --- | --- | --- |
| Modelos Whisper convertidos a CTranslate2 (tiny … large-v3) | La primera vez que transcribes con cada modelo, desde Hugging Face | MIT (modelos Whisper de OpenAI) | <https://huggingface.co/Systran> · <https://github.com/openai/whisper> |
| Python embebido, get-pip y faster-whisper | Solo si la transcripción no viene incluida (compilación con `-SinSTT` o desde el código) y aceptas instalarla | ver arriba | <https://www.python.org> · <https://bootstrap.pypa.io> · <https://pypi.org> |
| NVIDIA cuBLAS y cuDNN (`nvidia-cublas-cu12`, `nvidia-cudnn-cu12`) | Solo si pulsas «Instalar soporte GPU» (transcripción con tarjeta NVIDIA), desde PyPI | Licencias propietarias de NVIDIA (CUDA Toolkit EULA, cuDNN SLA); no se redistribuyen con Loquendo AI | <https://pypi.org/project/nvidia-cublas-cu12/> · <https://pypi.org/project/nvidia-cudnn-cu12/> |

## No incluido: lo instala cada usuario

| Componente | Para qué | Tipo de licencia | Enlace |
| --- | --- | --- | --- |
| **Loquendo TTS 7** y sus voces | Voces Loquendo | Software comercial de sus titulares | — |
| **Voces SAPI 5** (IVONA, ScanSoft, Microsoft…) | Voces SAPI 5 | Según cada fabricante | — |
| **BALCON** (Balabolka Command Line Utility), de Ilya Morozov | Voces SAPI 4 (Juan, Antonio…) | Freeware; no se redistribuye | <https://www.cross-plus-a.com/es/bconsole.htm> |
| Motores SAPI 4 (Microsoft, L&H TruVoice…) | Voces SAPI 4 | Según cada fabricante | — |
| **VEGAS Pro** | Edición final del proyecto exportado | Software comercial de MAGIX | <https://www.vegascreativesoftware.com> |
| **Ollama** y sus modelos | IA local | MIT (Ollama); cada modelo tiene la suya | <https://ollama.com> |
| **Google Gemini API** | IA en la nube | Términos de Google | <https://ai.google.dev> |
| **Anthropic Claude API** | IA en la nube | Términos de Anthropic | <https://www.anthropic.com> |
| **OpenAI API** | IA en la nube | Términos de OpenAI | <https://platform.openai.com> |

Loquendo AI no contiene código, voces ni datos de ninguno de estos productos. Solo los usa cuando están instalados o
configurados. El uso de las APIs en la nube se rige por los términos de cada proveedor y se cobra en tu propia cuenta.

## Herramientas de construcción (no se distribuyen)

| Herramienta | Uso | Licencia | Enlace |
| --- | --- | --- | --- |
| Inno Setup 6 | Crear el instalador | Licencia de Inno Setup (gratuita) | <https://jrsoftware.org/isinfo.php> |
| GitHub Actions: actions/checkout, actions/setup-dotnet, actions/upload-artifact, softprops/action-gh-release | Compilación y releases automáticos | MIT | <https://github.com/actions> · <https://github.com/softprops/action-gh-release> |

## Marcas

«Loquendo» es una marca de sus respectivos titulares. VEGAS Pro, Windows, SAPI, IVONA, Balabolka, Gemini, Claude,
ChatGPT y Ollama son marcas de sus dueños. Loquendo AI es un proyecto independiente de fans: no está afiliado ni
respaldado por ninguno de ellos, y los nombres se usan solo para describir la compatibilidad.

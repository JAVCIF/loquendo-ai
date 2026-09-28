<p align="center">
  <img src="src/LoquendoAI.App/Assets/loquendo-ai.png" alt="Loquendo AI" width="120">
</p>

<h1 align="center">Loquendo AI</h1>

<p align="center">
  Estudio de escritorio para crear videos al estilo «Loquendo» en Windows:<br>
  voces TTS clásicas, guion por escenas, preview en MP4, exportación editable a VEGAS Pro y un Director con IA.
</p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Descargar la última versión</b></a> ·
  <a href="#instalación">Instalación</a> ·
  <a href="#primeros-pasos">Primeros pasos</a> ·
  <a href="CONTRIBUTING.md">Contribuir</a> ·
  <a href="CHANGELOG.md">Novedades</a>
</p>

---

## Índice

- [Qué es](#qué-es)
- [Qué puede hacer](#qué-puede-hacer)
- [Cómo funciona por dentro](#cómo-funciona-por-dentro)
- [Requisitos](#requisitos)
- [Instalación](#instalación)
- [Primeros pasos](#primeros-pasos)
- [El lenguaje del Director](#el-lenguaje-del-director)
- [Director IA: proveedores y privacidad](#director-ia-proveedores-y-privacidad)
- [Exportar a VEGAS Pro](#exportar-a-vegas-pro)
- [Dónde se guarda cada cosa](#dónde-se-guarda-cada-cosa)
- [Compilar desde el código](#compilar-desde-el-código)
- [Estructura del repositorio](#estructura-del-repositorio)
- [Solución de problemas](#solución-de-problemas)
- [Contribuir](#contribuir)
- [Licencia y software de terceros](#licencia-y-software-de-terceros)

---

## Qué es

**Loquendo AI** automatiza el trabajo repetitivo de un video «tipo Loquendo»: escribir los diálogos, generar las voces
con motores TTS clásicos (Loquendo TTS 7, voces SAPI 4 y SAPI 5), colocar fondos y renders de personajes, poner
música y efectos, y montar todo en una línea de tiempo. El resultado se revisa en un **MP4 de preview** dentro del
programa y, cuando está listo, se **exporta a VEGAS Pro** como un proyecto con pistas, eventos y efectos
**editables**, para terminarlo a mano como siempre.

Además trae un **Director** que entiende un pequeño lenguaje de instrucciones (`[FONDO] cocina`, `Bart: hola`…) y un
**Director IA** que escribe o monta escenas completas a partir de una premisa, usando solo los recursos de tu
biblioteca.

> Es un proyecto de fans, hecho en español y para la comunidad. No está afiliado a Loquendo, Nuance, MAGIX ni a
> ningún proveedor de voces o de IA. No incluye ninguna voz comercial: usa las que tengas instaladas legalmente.

## Qué puede hacer

En la barra superior se crea o abre un proyecto (una carpeta con su base de datos), se hace una copia de seguridad,
se limpia la caché y se elige el tema claro u oscuro. Debajo hay cinco pestañas.

| Pestaña | Para qué sirve |
| --- | --- |
| **Inicio** | Guía rápida del flujo de trabajo. |
| **Biblioteca** | Registrar carpetas de recursos (fondos, renders, props, GIF, videos, música, SFX), clasificarlos por reglas de carpeta y etiquetarlos. La IA puede describir imágenes y reconocer música, memes y efectos por su nombre, o escuchando el audio con Gemini. |
| **Voice Lab** | Personajes y perfiles de voz: motor, voz, pitch, velocidad y volumen, con prueba inmediata. También decide qué voces aparecen en los selectores del resto del programa («Voces en los selectores…»). |
| **Diálogos** | Escribir, pegar o importar el guion completo de un episodio desde texto, Word, Excel o CSV. Se asigna la voz de cada línea (los NPC pueden tener cualquier voz), se generan y escuchan los audios, y se exporta todo a escenas. Incluye generación de diálogos con IA y un «prompt maestro» para usar con una IA web. |
| **Guion** | Episodios, escenas y bloques. Aquí están el **Editor** de bloques, la **Preview**, las **Voces grabadas**, el **Director (prompt)** y el **Director IA**. |

### Bloques de una escena

Una escena es una lista ordenada de bloques. Se editan en el Editor, se reordenan arrastrándolos en la tabla y el
tiempo de cada uno se calcula solo.

| Bloque | Qué hace |
| --- | --- |
| Diálogo / Narración | Una línea de voz. Usa la voz del personaje, un perfil o cualquier voz TTS de la lista, con ajustes propios. «NPC» es alguien sin personaje registrado. |
| Fondo | Imagen de fondo que cubre el cuadro, con zoom (hasta 300 %), desplazamiento, giro y animación. Se pueden mover sin bordes negros y continuar un paneo con «↳ Seguir desde el anterior». |
| Mostrar / Ocultar personaje | Renders (PNG con transparencia) con posición, encuadre automático (cuerpo entero, medio cuerpo, primer plano), animación de movimiento y giro. Un render sin personaje (un extra) también se puede ocultar. |
| Imagen / prop, Video | Props y GIF, videos encima o de fondo, con pantalla verde (croma), volumen y transiciones propias. |
| SFX, Música | Efectos (pueden esperar a terminar) y música de fondo, con volumen y duración (recorte, bucle o cambio de tempo). |
| Pausa, Transición | Silencios y transiciones: entrada, salida, cambio o cruce de capas, también con efectos de VEGAS. |
| Cámara | Acercamientos a un personaje (y lo sigue si se mueve), a quien habla o a un punto, o vuelta al plano general. |
| Cine | Barras negras de cine que entran y salen animadas. |
| Gesto | El clásico balanceo o rebote del render al hablar. |
| Desenfoque | Desenfoque gaussiano del fondo, de un personaje o de todo. |
| Comentario | Notas de dirección que no salen en el video. |

### Voces

- **Loquendo TTS 7** directamente, sin Balabolka, a través de un puente de 32 bits incluido.
- **Voces SAPI 5**, por ejemplo IVONA, Juan o cualquier voz instalada en Windows.
- **Voces SAPI 4** como Juan o Antonio, a través de **BALCON**, la consola de Balabolka. Se descarga aparte, ver
  [Requisitos](#requisitos).
- Cada línea puede usar una voz distinta. La lista de voces que aparece en los selectores se edita en Voice Lab.
- Los audios generados se guardan en caché: la misma línea con la misma voz no se vuelve a sintetizar.
- **Voces grabadas**: importa WAV de voces reales o de otros programas. La transcripción local (Whisper, sin
  internet tras bajar el modelo) escribe el texto de cada toma y la incorpora a la escena.

### Director e IA

- **Director (prompt)**: escribes instrucciones línea a línea y el programa busca los recursos en tu biblioteca,
  prepara un borrador revisable y lo aplica a la escena. Ver [el lenguaje](#el-lenguaje-del-director).
- **Director IA**: a partir de una premisa, la IA escribe la escena completa: fondo, música, renders, diálogos, SFX,
  cámara, cine, gestos y transiciones. Solo puede elegir recursos de tu catálogo y todo se valida antes de aplicarlo.
  - Tiene un modo que monta tus **voces grabadas** sin cambiar su audio.
  - Puede generar un **episodio** de varias escenas seguidas, con continuidad entre ellas.
- Proveedores: **Ollama** (local y gratuito), **Google Gemini**, **Anthropic Claude** y **OpenAI ChatGPT**. Puedes
  conectar varios a la vez y elegir el modelo en cada lista.

## Cómo funciona por dentro

```text
 ┌───────────────────────────── LoquendoAI.exe (WPF, .NET 10, x64) ─────────────────────────────┐
 │  Biblioteca · Voice Lab · Diálogos · Guion (Editor, Preview, Director, Director IA)          │
 │                                                                                              │
 │  LoquendoAI.Infrastructure  ── lógica sin interfaz (probada con pruebas automáticas)         │
 │    Persistence   proyecto = carpeta + SQLite (esquema versionado, copias de seguridad)       │
 │    Composition   SceneComposer ─► FFmpeg ─► MP4 de preview                                   │
 │                  VegasBridge   ─► escena.json + medios + script C# para VEGAS                │
 │                  LayerGeometry: la MISMA geometría para la preview y para VEGAS              │
 │    Director      lenguaje [FONDO]/[MOSTRAR]…, esquema JSON de la IA, planificación           │
 │    Tts           cliente del puente TTS y selector de voces                                  │
 └───────┬──────────────────────────────┬────────────────────────────────┬──────────────────────┘
         │ proceso aparte (x86)         │ proceso aparte                 │ HTTP
 ┌───────▼─────────────┐   ┌────────────▼─────────────────┐   ┌──────────▼─────────────────────┐
 │ tts-bridge          │   │ worker\python + faster-whisper│   │ Ollama (local) / Gemini /      │
 │ Loquendo TTS7 nativo│   │ transcripción local de WAV    │   │ Claude / ChatGPT (opcionales)  │
 │ SAPI5 · BALCON/SAPI4│   └──────────────────────────────┘   └────────────────────────────────┘
 └─────────────────────┘
```

- **El TTS va en un proceso de 32 bits** porque Loquendo TTS 7 y muchas voces SAPI son de 32 bits. El programa
  principal es de 64 bits y le pide cada línea al puente; varias líneas se generan en paralelo, con un tiempo máximo
  por línea.
- **La preview** es un MP4 que construye FFmpeg con un único grafo de filtros: capas, animaciones, cámara, barras de
  cine, gestos, desenfoque y mezcla de audio. Todo se calcula con la misma geometría que usa la exportación a VEGAS,
  así lo que ves en la preview es lo que obtienes en VEGAS.
- **La exportación a VEGAS no «aplana» nada.** Crea un proyecto con una pista por personaje o por clip. La cámara y
  el movimiento son keyframes de Pan/Crop, las barras de cine usan el «Cortador de galletas», el desenfoque usa el
  «Desenfoque gaussiano» y las transiciones son las de tu instalación. Todo se puede seguir editando en VEGAS.
- **El Director IA** recibe un catálogo resumido de tu biblioteca, con referencias cortas (`A1`, `A2`…) y
  descripciones, y responde con acciones JSON restringidas por un esquema. Cada acción se traduce al lenguaje del
  Director y se valida contra la biblioteca real antes de aplicarla.
- **Un proyecto es una carpeta** con su base de datos SQLite, el caché de voces, los MP4 y las exportaciones. Tus
  recursos originales no se copian ni se modifican: el proyecto solo guarda dónde están.

Más detalle en [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Requisitos

| Componente | ¿Hace falta? | Notas |
| --- | --- | --- |
| Windows 10 u 11 de 64 bits | **Sí** | |
| Loquendo TTS 7 y/o voces SAPI 5 | **Sí**, alguna voz | Instálalas tú; Loquendo AI no incluye voces. |
| [BALCON](https://www.cross-plus-a.com/es/bconsole.htm) (consola de Balabolka) | Solo para voces **SAPI 4** | Descarga el ZIP oficial y **extrae todo** en `tools\balcon\` dentro de la carpeta del programa. Más detalles en `tools\balcon\README.txt`. |
| FFmpeg | Ya incluido | En `tools\ffmpeg`. Desde el código fuente, ponlo en el PATH. |
| .NET 10 | Ya incluido | El `.exe` lleva .NET dentro. |
| Python y faster-whisper | Ya incluidos | En `worker\python`, para la transcripción local. El modelo de Whisper se descarga la primera vez que transcribes. |
| VEGAS Pro | Opcional | Para terminar el video. Hay scripts para VEGAS 12–13 y 14 o superior. |
| Ollama, o una clave de Gemini, Claude u OpenAI | Opcional | Solo para las funciones de IA. |

## Instalación

Descarga desde [**Releases**](../../releases/latest) una de estas dos opciones:

- **`LoquendoAI_vX.Y.Z_setup.exe`** es el instalador. Se instala para tu usuario, sin permisos de administrador, en
  `%LOCALAPPDATA%\Programs\Loquendo AI`, con accesos directos y desinstalador.
- **`LoquendoAI_vX.Y.Z_portable_win-x64.zip`** es la versión portable. Descomprímela donde quieras, incluso en una
  memoria USB, y abre `LoquendoAI.exe`. Toda su configuración queda en la carpeta `datos\` junto al `.exe`.

Después:

1. Instala **Loquendo TTS 7** y/o tus voces SAPI 5.
2. Si quieres voces **SAPI 4**, descarga [BALCON](https://www.cross-plus-a.com/es/bconsole.htm) y extrae todo el ZIP en
   `tools\balcon\` dentro de la carpeta del programa.
3. Abre Loquendo AI. Nada más: la transcripción local y FFmpeg ya vienen dentro.

> Windows SmartScreen puede avisar la primera vez porque el ejecutable no está firmado. Pulsa «Más información →
> Ejecutar de todas formas» si lo descargaste de este repositorio.

## Primeros pasos

1. **Crear proyecto**, en la barra superior: escribe un nombre y elige una carpeta vacía.
2. **Biblioteca → Agregar fuente…** Elige tu carpeta de recursos. El asistente propone reglas por subcarpeta: fondos,
   renders de personaje, música, SFX… y la escanea. Los renders deberían ser PNG con transparencia y, a ser
   posible, en una carpeta con el nombre del personaje.
3. **Voice Lab**: crea los personajes, prueba voces, guarda un perfil para cada uno y **asígnalo**. Un personaje sin
   perfil habla con la voz «NPC» (Jorge de Loquendo por defecto).
4. **Guion**: crea un episodio y una escena. En el **Editor** añade bloques (fondo, mostrar personaje, diálogo, SFX…),
   o escribe instrucciones en **Director (prompt)**:
   ```text
   [FONDO] cocina
   [MOSTRAR] Bart | feliz | izquierda
   Bart: ¡Hola a todos!
   [SFX] golpe | esperar
   [CAMARA] Bart | zoom=1.6 | duracion=300
   Bart: ¿Qué fue eso?
   [CAMARA] general
   ```
5. **Preview → ▶**: genera las voces que falten y el MP4, y lo reproduce. La línea de tiempo se puede arrastrar.
6. **Exportar a VEGAS** cuando esté listo (ver [abajo](#exportar-a-vegas-pro)).

Para escribir un episodio largo, empieza por **Diálogos**: pega o importa el guion (`Nombre: texto`, una línea por
intervención), revisa las voces, genera los audios y pulsa **Exportar a escenas**. Luego el **Director IA**, en
modo voces grabadas, pone la parte visual.

## El lenguaje del Director

Una instrucción por línea. Las opciones van después de `|`. Las líneas que empiezan con `#` son comentarios.

| Instrucción | Ejemplo |
| --- | --- |
| Fondo | `[FONDO] ciudad \| zoom=1.5 \| animar x=-300 \| animar ms=3000` |
| Mostrar render | `[MOSTRAR] Bart \| enojado \| medio cuerpo \| derecha \| animar x=-250 \| animar ms=2400` |
| Render sin personaje (extra) | `[MOSTRAR] NPC \| guardia \| derecha` … `[OCULTAR] NPC \| guardia` |
| Ocultar | `[OCULTAR] Bart` |
| Diálogo | `Bart: texto` · `NPC: texto` (voz en off) · `[NARRACION] texto` |
| Imagen / GIF / video | `[IMAGEN] explosión \| duracion=2000` · `[VIDEO] meme \| capa=sobre \| volumen=60` |
| Audio | `[MUSICA] tema tranquilo \| volumen=20` · `[SFX] golpe \| esperar=si \| volumen=70` |
| Pausa y transición | `[PAUSA] 500` · `[TRANSICION] cruce \| duracion=500 \| vegas=flash \| capas=fondo` |
| Cámara | `[CAMARA] Bart \| zoom=1.5 \| duracion=300 \| enfoque=cara` · `[CAMARA] habla` · `[CAMARA] general` |
| Cine | `[CINE] mostrar \| estilo=cerrado \| duracion=800` … `[CINE] quitar` |
| Gesto | `[GESTO] Bart \| balanceo` · `[GESTO] habla \| balanceo+rebote` … `[GESTO] habla \| quitar` |
| Desenfoque | `[DESENFOQUE] fondo \| suavizar \| duracion=600` … `[DESENFOQUE] fondo \| quitar` |

Opciones visuales comunes: `ancho`, `alto`, `x`, `y`, `rotacion`, `voltear h`, `voltear v`, `duracion`,
`transicion=heredar|corte|fundido|disolvente|flash|barrido|plugin`.

Los recursos se buscan por nombre, carpeta y etiquetas en tu biblioteca. **Copiar escena** hace lo contrario: convierte
una escena en estas líneas, o en JSON completo, para editarla o pasársela a otra IA.

## Director IA: proveedores y privacidad

- **Configurar IA** (en Biblioteca o en Director IA) conecta **Ollama**, **Gemini**, **Claude** y **ChatGPT**. Elige
  qué modelos aparecen en las listas y los predeterminados.
- Las **claves de API se guardan cifradas con Windows (DPAPI)** para tu usuario, fuera de los proyectos. También se
  pueden dar con `GEMINI_API_KEY`, `ANTHROPIC_API_KEY` u `OPENAI_API_KEY`.
- **Qué se envía**: con Ollama, nada sale de tu PC. Con un proveedor en la nube se envía la premisa, un catálogo
  resumido (nombres, carpetas y descripciones de los recursos elegidos, no los archivos) y, al analizar recursos,
  las imágenes o audios que tú selecciones. El uso de la API se cobra en tu cuenta de ese proveedor.
- **Prompt maestro**: las instrucciones base del Director IA se pueden ver y editar por proyecto. Las reglas fijas
  (formato, NPC, modo) se añaden siempre.

## Exportar a VEGAS Pro

1. En **Guion → Preview** elige la resolución (720p o 1080p), el modo y las pistas de audio, y pulsa **Exportar a
   VEGAS**. **Exportar episodio** hace lo mismo con todas las escenas seguidas, con una región por escena.
2. Se abre una carpeta con `escena.json`, `LEEME.txt`, los medios preparados y los scripts.
3. En VEGAS, con un proyecto **nuevo y vacío**: **Herramientas → Scripts → Ejecutar script** y elige el script de tu
   versión. VEGAS guarda un `.veg` en esa carpeta.

La cámara, el movimiento, los gestos, las barras de cine y el desenfoque quedan como keyframes y efectos editables. En
`scripts\vegas\` hay diagnósticos para comprobar qué efectos y transiciones tiene tu VEGAS. **Transiciones VEGAS…**,
en el Director IA, importa tu catálogo para que la IA las use.

## Dónde se guarda cada cosa

| Qué | Instalado | Portable |
| --- | --- | --- |
| Configuración, claves cifradas, tema, `errores.log`, selector de voces | `%LOCALAPPDATA%\LoquendoAI\` | `datos\` junto al `.exe` |
| Modelos de Whisper | caché de Hugging Face del usuario | `datos\modelos-stt\` |
| Proyecto (base de datos, voces, MP4, exportaciones, copias) | la carpeta del proyecto | la carpeta del proyecto |
| Tus recursos | donde estén: nunca se copian ni se modifican | igual |

## Compilar desde el código

Requisitos: Windows, el [SDK de .NET 10](https://dotnet.microsoft.com/download) y FFmpeg en el PATH para la preview.

```powershell
git clone https://github.com/JAVCIF/loquendo-ai.git
cd loquendo-ai
.\scripts\run-loquendo-ai.cmd        # publica el puente TTS x86, compila y abre la app
.\scripts\pruebas.cmd                # pruebas automáticas (el código de salida es el número de fallos)
.\scripts\pruebas.cmd GeometryTests  # solo las que contienen ese nombre
```

**Crear la versión para distribuir**, lo mismo que hace el release:

```powershell
.\scripts\publicar.ps1 -Pruebas -Instalador
```

Deja en `artifacts\` el `.exe` de un archivo, la carpeta portable con su `.zip` y el instalador, si tienes
[Inno Setup](https://jrsoftware.org/isdl.php) 6 o posterior. Descarga una vez Python embebido, faster-whisper y
FFmpeg (serie estable 8.1 fijada). Opciones: `-SinSTT`, `-SinFFmpeg`, `-FFmpegSerie 9.0` (otra serie de FFmpeg),
`-DependeDeNet` (un `.exe` pequeño que necesita .NET instalado).

**Releases automáticos**: al subir una etiqueta `vX.Y.Z`, GitHub Actions ejecuta `publicar.ps1 -Instalador` en
Windows y publica el instalador y la portable en el release.

<details>
<summary>Variables de entorno para ajustes avanzados</summary>

| Variable | Efecto |
| --- | --- |
| `LOQUENDO_AI_TTS_PARALLEL` | Líneas TTS generadas a la vez. |
| `LOQUENDO_AI_TTS_TIMEOUT_SECONDS` | Tiempo máximo por línea TTS. |
| `LOQUENDO_AI_TTS_BRIDGE` | Ruta de un puente TTS propio. |
| `LOQUENDO_AI_BALCON` | Ruta completa a `balcon.exe`. |
| `LOQUENDO_AI_STT_COMPUTE` | `float16`, `int8_float16` o `int8` para Whisper. |
| `LOQUENDO_AI_PROBE_CACHE` | Desactiva (`0`) la caché de mediciones de medios. |
| `LOQUENDO_AI_OLLAMA_NUM_CTX` | Contexto de Ollama. |
| `LOQUENDO_AI_GEMINI_BASE_URL`, `LOQUENDO_AI_CLAUDE_BASE_URL`, `LOQUENDO_AI_OPENAI_BASE_URL` | Endpoints alternativos. |
| `LOQUENDO_AI_DIRECTOR_LEGACY` | Salida antigua de la IA en líneas de texto. |

</details>

## Estructura del repositorio

```text
src/
  LoquendoAI.Core/            modelos (bloques, escenas, recursos…)
  LoquendoAI.Infrastructure/  lógica: base de datos, composición, VEGAS, Director, diálogos, TTS
  LoquendoAI.App/             aplicación WPF (ventanas y temas)
  LoquendoAI.TtsBridge32/     puente TTS de 32 bits (Loquendo TTS7, SAPI5, BALCON)
  LoquendoAI.Cli/             utilidades de línea de comandos
tests/LoquendoAI.Tests/       pruebas automáticas (consola, sin dependencias externas)
worker/stt/                   transcriptor local (faster-whisper)
scripts/                      compilar, probar, publicar, instalador y scripts de VEGAS
tools/balcon/                 aquí va BALCON (no se redistribuye)
docs/                         arquitectura, pruebas guiadas por versión e historial de desarrollo
```

## Solución de problemas

- **«No se encontraron voces» o una voz no suena.** Comprueba que la voz esté instalada y que sea de 32 bits si es
  SAPI. En Voice Lab, «▶ Escuchar» muestra el error exacto del motor.
- **Las voces SAPI 4 no aparecen.** Falta BALCON: descárgalo de <https://www.cross-plus-a.com/es/bconsole.htm> y extrae
  el ZIP completo, no solo `balcon.exe`, en `tools\balcon\`.
- **La transcripción falla con un error de DLL.** Instala el «Microsoft Visual C++ 2015-2022 Redistributable (x64)».
  Desde el código fuente, la app ofrece instalar Python y faster-whisper la primera vez.
- **La preview no se genera.** Mira `errores.log` (ver [Dónde se guarda](#dónde-se-guarda-cada-cosa)). Desde el
  código fuente, comprueba `ffmpeg -version` en una consola.
- **La IA dice «sin cuota» o «sin crédito».** El lote se detiene y conserva lo ya hecho. Revisa el saldo o el límite
  de tu cuenta y vuelve a pulsar; lo ya analizado se omite.

¿Algo más? Abre un [issue](../../issues/new/choose).

## Contribuir

¡Las contribuciones son bienvenidas! Antes de abrir un issue o un pull request, lee
[**CONTRIBUTING.md**](CONTRIBUTING.md): explica qué datos incluir en un reporte, cómo preparar el entorno, las
convenciones del código y la lista de comprobación de un PR.

## Licencia y software de terceros

El código de Loquendo AI se publica bajo la **[licencia MIT](LICENSE)**.

Los paquetes publicados incluyen software de terceros con sus propias licencias: el runtime de .NET, SQLite, Python,
faster-whisper y sus dependencias, y FFmpeg (GPL). Otras cosas que usa **no se incluyen** y las instalas tú:
Loquendo TTS, voces SAPI, BALCON, VEGAS Pro y los servicios de IA. El detalle, con enlaces y licencias, está en
**[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)**.

«Loquendo» es una marca de sus respectivos titulares. VEGAS Pro, Windows y demás nombres son marcas de sus dueños. Se
mencionan solo para describir la compatibilidad.

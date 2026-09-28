# Historial acumulado de hotfixes y entregas

En cada nueva entrega, agrega una sección a este archivo; no crees otro `HOTFIX_*.md`. El contenido original de los hotfixes anteriores se conserva a continuación.

## v1.4.2 — Primera publicación en GitHub: instalador y portable completos
- **Todo incluido**: el instalador y la portable traen .NET, la transcripción local (Python + faster-whisper, con el runtime de Visual C++) y ahora también **FFmpeg** (`tools\ffmpeg`). La app lo usa antes que cualquier otro FFmpeg del PC. Tú solo instalas Loquendo TTS 7 y/o tus voces SAPI 5 y, para SAPI 4, **BALCON** (<https://www.cross-plus-a.com/es/bconsole.htm>).
- **Releases automáticos**: al subir una etiqueta `vX.Y.Z`, GitHub Actions ejecuta `scripts\publicar.ps1 -Instalador` en Windows y publica el instalador y la portable. Otro flujo compila y pasa las pruebas en cada push y pull request.
- `publicar.ps1`: `-SinFFmpeg`, `-FFmpegZip` y `-FFmpegSerie`. Descarga la compilación GPL de una serie estable fijada (8.1 por defecto) desde una versión fechada de BtbN/FFmpeg-Builds, y deja en `tools\ffmpeg\README.txt` la versión exacta, el commit y el enlace al código fuente. El paquete incluye `LICENSE` y `THIRD_PARTY_NOTICES.md`.
- **Documentación**: README nuevo y detallado (qué hace, cómo funciona, instalación, lenguaje del Director, IA y privacidad, VEGAS, compilación), `CONTRIBUTING.md` (issues, pull requests, convenciones y pruebas), plantillas de issue y PR, **licencia MIT** y avisos de software de terceros.
- Los documentos de desarrollo antiguos pasaron a `docs/historial/` y las pruebas guiadas por versión a `docs/pruebas/`.
- Los mensajes de «falta BALCON» incluyen el enlace de descarga.

## v1.4.1 — Ocultar renders sin personaje (NPC), arrastrar bloques, Juan (SAPI5) y Jorge en Diálogos
- **Ocultar renders sin personaje**: «Mostrar personaje» admitía un render sin personaje (un extra, un NPC), pero «Ocultar personaje» solo ofrecía personajes y ese render no se podía quitar.
  - Ahora la lista de «Ocultar personaje» incluye los renders mostrados antes sin personaje, con el nombre del recurso («NPC · guardia»).
  - Si después le asignas personaje a ese «Mostrar», el «Ocultar» queda atado al personaje: desaparece el nombre del recurso y se guarda el personaje. También vale si el cambio viene del Director.
  - Dos extras con el mismo render se ocultan uno por uno. Si el mismo render está como NPC y como personaje (Bart), se oculta el NPC, no Bart.
  - La preview, la exportación a VEGAS, la duración de la escena y la continuidad entre escenas usan la misma regla.
- **Director**: `[MOSTRAR] NPC | guardia | derecha` muestra un render sin personaje y `[OCULTAR] NPC | guardia` lo quita. «Copiar escena» escribe esas líneas.
- **Director IA y prompt maestro**:
  - La IA recibe algunos renders sin personaje como «NPC» (siempre los que ya están en la escena) y los muestra con «mostrar» personaje NPC.
  - Los retira con la nueva acción «ocultar_npc», usando el mismo render.
  - Los NPC no son objetivo de cámara, gesto ni desenfoque.
  - La regla va fija junto al prompt maestro, así que también vale si lo personalizaste. Se ve en «Prompt maestro…» del Director IA, pestaña «Enviado en modo historia» (o «voces»).
- **Bloques de escena**: los bloques se reordenan **arrastrándolos** en la tabla, uno o varios seleccionados (conservan su orden). Una línea roja marca dónde quedan, arriba o abajo de la fila, y la tabla se desplaza sola cerca de los bordes. ↑/↓ siguen funcionando. La tabla ya no se ordena por columnas, para que lo que ves sea el orden del guion.
- **Voces**: la voz **Juan de SAPI5** sale marcada por defecto en los selectores, junto con las IVONA. Juan y Antonio se reconocen como nombre completo, así que «Juana» no cuenta.
- **Diálogos**: la voz NPC por defecto se llama «TTS7 · Jorge», sin «(voz NPC)», y no se repite en la lista. «Asignar voz NPC» sigue dejando Jorge por defecto.
- **Pruebas**: 88 (5 nuevas: a qué apunta un «Ocultar» de render, la escena y VEGAS con un NPC oculto y otro en pantalla, las líneas NPC del Director, el esquema de la IA con «ocultar_npc», y dónde caen los bloques arrastrados). Además:
  - una revisión aparte de todo el cambio; ya se corrigió lo que encontró;
  - el validador de XAML y la compilación contra los stubs de WPF.
  - Pendiente en Windows: probar el arrastre y la lista de «Ocultar personaje» en la app.

## v1.4.0 — Transcripción local incluida, NPC con voz real, más voces (SAPI4/SAPI5) y fondos con zoom
- **Transcripción local ya instalada en la versión publicada**:
  - `scripts\publicar.cmd` mete en la carpeta portable (y con ella en el instalador) un **Python embebido oficial con faster-whisper** en `worker\python` (≈ 300 MB). Quien use el .exe o la portable no ejecuta nada: solo instala Loquendo TTS7 y, si quiere voces SAPI4, pone BALCON.
  - El modelo de Whisper elegido se descarga solo la primera vez que se transcribe. En la portable queda en `datos\modelos-stt`, junto al .exe.
  - Si falta (paquete hecho con `-SinSTT`, carpeta borrada o ejecución desde el código), al pulsar Transcribir la app **ofrece instalarla ella misma**: usa el Python del equipo (3.9+ de 64 bits) o, si no hay, descarga el embebido. El progreso sale en la barra de estado y «Cancelar» la detiene; una instalación cancelada no deja un Python a medias y se repara sola la vez siguiente.
  - `scripts\stt-setup.ps1` hace lo mismo a mano (ya no exige tener Python instalado).
- **NPC ya no aparece como voz**:
  - Un bloque nuevo de **Diálogo o Narración** queda con Personaje **«NPC»** y en Voz la voz real: **«TTS7 · Jorge (voz NPC)»**, o «Su perfil · Bart» si eliges un personaje con perfil. Al cambiar de personaje, esa primera opción se renombra sola.
  - La línea sigue al perfil: si en Voice Lab cambias la voz del NPC o le das perfil al personaje, esas líneas cambian también. Para fijar una voz concreta, elígela de la lista.
  - Un diálogo se puede guardar con Personaje «NPC» (alguien sin personaje registrado, voz en off). «Copiar escena» (Director) lo escribe `NPC: texto` y el Director lo acepta.
  - En Diálogos la voz predeterminada de los NPC se llama igual («TTS7 · Jorge (voz NPC)»).
- **Más voces en los selectores**: en «Voz» del Editor, en Diálogos y en Voces grabadas salen, además de todas las de Loquendo TTS7, **Juan y Antonio (SAPI4 vía BALCON)** y las **IVONA (SAPI5)**. Cualquier línea puede usar cualquiera de ellas; su prosodia neutra es la del motor (y los overrides de pitch/vel./vol. siguen funcionando).
  - **Voice Lab › «Voces en los selectores…»**: todas las voces instaladas por motor con una casilla, ▶ para escucharlas, «Buscar voces instaladas» (para cuando instales voces nuevas), «Añadir a mano» (una voz que el motor no listó) y «Predeterminadas». Se guarda por PC (`selector-voces.json`, en `datos\` en la portable).
  - Al abrir un proyecto solo se espera a la lista de TTS7; SAPI4 y SAPI5 se consultan en segundo plano con un límite de tiempo.
- **Fondos con zoom y movimiento continuo** (caminatas, paneos, saltos):
  - Fondo: nuevo **Zoom** en el Editor (100–300 %), o Ancho/Alto hasta 3840×2160. Un fondo más grande que el cuadro se puede desplazar o animar **sin bordes negros** y **sin cambiar de escala** al empezar a moverse. Antes un fondo que se animaba se ampliaba de golpe para cubrir el recorrido.
  - Al guardar un fondo, si el zoom no alcanza para su desplazamiento o su paneo, la barra de estado dice qué zoom usar.
  - **«↳ Seguir desde el anterior»** (junto a Animar): el bloque empieza donde terminó el bloque anterior del mismo fondo, personaje, imagen o video (tamaño/zoom, posición y giro finales). Solo queda poner el ΔX/ΔY del nuevo tramo.
  - Director: `[FONDO] ciudad | zoom=1.5 | animar x=-300 | animar ms=3000`. «Copiar escena» escribe `zoom=` para esos fondos.
  - La preview y VEGAS (Pan/Crop y medio normalizado) usan la misma caja; los fondos de hasta 1280×720 se ven exactamente como antes.
- **Pruebas**: 83 (8 nuevas: selector de voces por defecto, elecciones y guardado, voz directa de cualquier motor, fondo con zoom quieto/en movimiento con la misma escala, crecimiento solo cuando hace falta, límites de tamaño, `zoom=` del Director y un render de la preview que comprueba que el paneo no salta ni deja negro). Además:
  - una revisión aparte de todo el cambio; ya se corrigió lo que encontró;
  - el validador de XAML y la compilación contra los stubs de WPF.
  - Pendiente en Windows: `dotnet build`, probarlo y ejecutar `scripts\publicar.cmd` (aquí no hay PowerShell ni WPF).

## v1.3.0 — Ícono, ChatGPT, IA de medios en la Biblioteca, mensajes con el tema y versión publicable
- **Ícono de la app**: la bolita roja y blanca con la L. Sale en el .exe (Explorador, Administrador de tareas), en la barra de tareas, en Alt+Tab y en la esquina de todas las ventanas, también las secundarias.
- **ChatGPT (OpenAI)** en «Configurar IA», junto a Ollama, Gemini y Claude: clave cifrada (o `OPENAI_API_KEY`), lista de modelos de chat (se marcan al principio los principales: GPT-5, GPT-4.1, GPT-4o, o3/o4), salida estructurada con JSON Schema, imágenes en los modelos con visión y nivel de razonamiento para GPT-5 y la serie o. Sirve para el Director IA, el análisis de imágenes y los diálogos. La API se paga aparte de ChatGPT Plus.
- **Biblioteca · IA**:
  - «IA: analizar imágenes seleccionadas» sigue como antes (imágenes y GIF con el modelo de visión), ahora también por filtro. Con 2 o más filas seleccionadas analiza esas. Con una sola seleccionada y un filtro activo, pregunta si analizar el filtro entero o solo esa. Sin selección, pregunta si analizar todo el filtro, también lo que no se ve. Máximo 500 por lote; si son más de 20, pide confirmación.
  - Nuevo **«IA: analizar medios seleccionados»**:
    - Imágenes: con el modelo de visión.
    - Música, SFX, videos y GIF: por su **nombre** (y carpeta) con el modelo del Director, 25 archivos por petición. Así gasta muy pocos tokens y reconoce canciones y memes conocidos.
    - Los nombres que no dicen nada («track01», «sfx_023», «IMG_2041») no se envían.
    - Los audios que el nombre no identifica pueden **escucharse**: primero se guarda lo que salió por el nombre y el programa pregunta si enviar hasta 30 s de cada uno a **Gemini**, que entiende audio (su API tiene nivel gratuito; no hace falta ElevenLabs). Necesita FFmpeg.
    - No repite lo ya hecho y conserva lo mejor: un GIF descrito por su imagen o un audio ya escuchado no se re-describen por el nombre.
  - La descripción de la IA **aparece en «Editar etiquetas»**, marcada «escrito por la IA». Si la cambias, pasa a ser tuya; si no, un nuevo análisis la puede actualizar.
  - **Sin cuota o sin crédito** (429, `insufficient_quota`, crédito de Claude agotado, límite diario de Gemini): el lote se detiene, se conserva todo lo ya analizado y un mensaje explica qué pasó. Al volver a pulsar se omite lo hecho. Un archivo que no está en su carpeta se salta sin contar como fallo. Tres fallos seguidos detienen el lote.
- **Mensajes con el tema**:
  - Todos los avisos, errores y preguntas del programa usan ahora una ventana propia en lugar de la de Windows (que siempre era blanca). Siguen el tema claro u oscuro con la barra de título oscura, llevan botones en español (Aceptar / Sí / No / Cancelar) y Ctrl+C copia el texto.
  - Corregidos los botones blancos de las ventanas de Diálogos en tema oscuro.
- **Publicar**:
  - `scripts\publicar.cmd` (o `publicar.ps1`) crea la versión final en `artifacts\`:
    - `LoquendoAI.exe` en un solo archivo, con .NET dentro;
    - el puente TTS x86 en `tts-bridge\`;
    - la carpeta **portable** y su `.zip`;
    - con `-Instalador` y Inno Setup 6, un instalador.
    - `-Pruebas` pasa antes las pruebas; `-DependeDeNet` hace un .exe pequeño que necesita .NET 10.
  - **Versión portable**: con `portable.txt` junto al .exe, toda la configuración (tema, tamaños, IA, claves, registros, cachés, catálogo de VEGAS) va a `datos\` en esa carpeta. Sin ese archivo se usa `%LOCALAPPDATA%\LoquendoAI`, como siempre.
- **Pruebas**: 75 (3 nuevas: tipos de medio, nombres sin información, y lote por nombre con su respuesta). Además:
  - una revisión aparte de todo el cambio; ya se corrigió lo que encontró;
  - el validador de XAML y la compilación contra los stubs de WPF.
  - Pendiente en Windows: `dotnet build`, probarlo y ejecutar `scripts\publicar.cmd` (aquí no hay PowerShell ni WPF).

## v1.2.0 — Módulo de Diálogos, perfil NPC y botones para detener el audio
- Nueva pestaña **Diálogos** (entre Voice Lab y Guion) para escribir, importar o generar con IA todas las líneas de un episodio, generar sus voces y mandarlas a escenas.
  - **Tabla**: ▶ · # · Tipo (Diálogo, Narración o «▬ Escena», que es un corte) · Quién habla · Voz asignada · Pitch / Vel. / Vol. · Diálogo · Comentario · Estado · Duración.
    - **Quién habla**: un personaje registrado usa su perfil de Voice Lab; el botón ✎ abre ese perfil en Voice Lab. Cualquier otro nombre es un **NPC**: su voz se elige ahí mismo (la del perfil NPC, cualquier voz TTS7 o un perfil guardado). Escribir «Narrador» convierte la fila en narración.
    - **Pitch, Vel. y Vol.** ajustan solo esa línea, en cualquier fila, también las de personajes con perfil. El perfil no cambia: su base se edita en Voice Lab. Vacío = el valor del perfil.
    - **Estado**: Pendiente / ✓ Audio listo / Cambió. Al pasar el ratón se ve la voz exacta y los avisos (NPC sin registrar, personaje sin perfil, texto vacío…).
  - **Varios guiones de diálogos** por proyecto (Nuevo, Renombrar, Eliminar). Se guardan solos en `<proyecto>\dialogos\*.json`. Un archivo que no se pueda leer se aparta como `.bak` en vez de sobrescribirse.
  - **Importar** desde .txt/.md, Word (.docx, párrafos y tablas en orden), Excel (.xlsx, primera hoja) o CSV, o **pegar / escribir** el texto. Hay dos modos:
    - **Formato recomendado**: `Nombre: texto`. Una línea sin nombre es narración. `[ESCENA] título` marca un corte. `(acotación)` va a Comentario. También sirve `Nombre (enojado): …` y `Bart dice: …`.
    - **Modo análisis**: guion de cine (NOMBRE en su línea, INT./EXT.), novela («—Hola —dijo Bart»), chats, tiempos, numeraciones, `Nombre - texto` y encabezados «Escena 2: …».
    - Una frase normal con dos puntos («Esto es una frase: …», «Homero gritó: …») no se toma por un nombre.
    - «Formato recomendado…» lo explica con un ejemplo que se puede copiar. «Exportar .txt» guarda el guion en ese formato, y se puede volver a importar.
  - **Prompt maestro…** pregunta qué personajes participan (más otros secundarios), cuántas líneas quieres y la historia, y arma el prompt para una IA web (ChatGPT, Gemini, Claude…). Su respuesta se pega con «Pegar / escribir texto…». La base del prompt se puede editar por proyecto (`dialogos\prompt-maestro.txt`).
  - **Generar diálogos con IA**: usa ese mismo prompt con el modelo elegido (los del Director IA). Solo escribe los diálogos, no monta escenas. Pide hasta 150 líneas por vez; con la tabla llena, continúa la historia o la reemplaza.
  - **Audio**:
    - «Generar audios»: varias líneas a la vez, en la caché de voces (el mismo archivo que usará la escena).
    - «Regenerar selección».
    - ▶ en cada fila: si la línea no tiene audio, lo genera primero.
    - «▶ Reproducir todo» de corrido desde la fila seleccionada, y «■ Detener».
  - **Exportar a escenas**: siempre crea escenas NUEVAS al final del episodio elegido (o de uno nuevo). Las líneas entran como voces grabadas: el WAV, el texto y la voz con la que se generaron, así que «Regenerar con TTS» usa esa misma voz.
    - **Una escena con todas**: para Director IA → «Montar voces grabadas» → «Generar episodio», que las reparte.
    - **Repartir en escenas**: corta en cada «▬ Escena» y parte en trozos iguales lo que pase de las líneas por escena (40 como máximo).
    - Los NPC con nombre se crean como personajes sin perfil y hablan con su voz de NPC.
    - Las acotaciones van con cada toma y el Director IA las ve.
  - **Límites**: 480 líneas por guion (12 escenas × 40, lo que admite el Director IA por episodio). Máximo 40 líneas por escena.
  - Más: + Línea, + Corte de escena, Duplicar, ↑ ↓, Eliminar (también con Supr), asignar quién habla, voz NPC o pitch/vel./vol. a varias filas, buscar y un resumen (líneas, escenas, personajes, audio listo y duración total).
- **Perfil de voz «NPC»**:
  - Cada proyecto lo tiene (se crea al abrirlo) y aparece en Voice Lab. Se puede editar pero no eliminar. Su voz por defecto es Jorge (Loquendo TTS7).
  - Es la voz de quien no tiene perfil propio: NPC, narrador y personajes creados como etiqueta. Por eso una línea ya no se queda sin voz con «no tiene perfil de voz»: el Director ya no rechaza diálogos de personajes sin perfil.
  - Una línea puede darle cualquier voz TTS7 conservando el pitch, la velocidad y el volumen del perfil NPC.
- **Detener audio**:
  - ■ junto al ▶ del Editor (SFX / música), junto a «▶ Audio» de los bloques y en Voces grabadas.
  - Antes el sonido solo paraba al reproducir otro.
- Sin cambios en la base de datos. Los proyectos existentes suenan igual: el perfil NPC solo se usa donde antes faltaba la voz.
- **Pruebas**: 72. Seis nuevas del módulo:
  - formato recomendado;
  - análisis (guion de cine, novela, chat);
  - tablas de Word, Excel y CSV;
  - ida y vuelta por texto;
  - reparto en escenas;
  - respuesta de la IA y prompt web.
- **Verificación en Linux**: el validador de XAML revisa también los eventos nuevos y la App compila contra los stubs de WPF. Pendiente en Windows: `dotnet build` y probarlo (PRUEBA_1.2.0.txt).

## v1.1.1 — Editor con los parámetros de Cámara, Cine, Gesto y Desenfoque; línea de tiempo libre en la Preview
- Editor del bloque: los bloques Cámara, Cine, Gesto y Desenfoque ya se ajustan desde el Editor, sin pasar por el Director (prompt). Cada tipo muestra su panel:
  - Cámara: modo (encuadrar al personaje, seguir a quien habla, punto del cuadro o plano general), zoom (1–3), movimiento en ms (0 = corte), enfoque cara/cuerpo y ajuste X/Y.
  - Cine: mostrar o quitar las barras, estilo cerrado/abierto, transición en ms y capas: todas, solo personajes o personajes elegidos (se marcan en una lista).
  - Gesto: este personaje una vez, quien habla desde aquí o dejar de gesticular; movimientos («balanceo», «rebote», «balanceo+rebote», «rebote, balanceo»… se puede escribir), ángulo, estirar %, lado, velocidad y eje.
  - Desenfoque: a quién (el personaje elegido, fondo, todos los personajes, imágenes, videos o todo el cuadro), estilo Suavizar 0,02 / Ligero 0,01 / Nítido 0 / Personalizado, valor y transición en ms.
  - Al seleccionar un bloque hecho con el Director, el panel muestra lo que dice su línea; al guardar se escriben los mismos parámetros que la línea del Director, así que la preview, VEGAS y «Copiar escena» lo leen igual. Si falta algo (por ejemplo, encuadrar sin personaje), un aviso lo dice y no se guarda. Se aceptan decimales con coma o con punto.
  - El combo «Personaje» se desactiva cuando el modo no lo usa (quien habla, punto, plano general, un objetivo de desenfoque sin personaje).
  - Los campos que el modo no usa (zoom y X/Y en plano general, todo menos el modo en «Dejar de gesticular») quedan en gris y no se comprueban al guardar. «Punto del cuadro» empieza en zoom 1.3, como `[CAMARA] punto`. La lista de personajes del Cine se actualiza si añades o renombras uno.
- Preview:
  - La línea de tiempo ya se mueve a cualquier punto. Antes el reloj de reproducción la devolvía a su sitio cada 150 ms y pulsar la pista avanzaba 1 ms. Ahora pulsar en cualquier punto salta ahí y arrastrar recorre el video mostrando el fotograma, también en pausa o detenido. Si estaba reproduciendo, sigue desde el nuevo punto al soltar. Con el teclado: ← / → mueven 1 s y RePág / AvPág 5 s.
  - La línea de tiempo tiene una bolita roja en vez del rectángulo, con el tramo recorrido en rojo, y está en una barra de controles con el color del tema junto al tiempo (con cifras de ancho fijo para que no baile).
  - Los deslizadores de Voice Lab (pitch, velocidad, volumen) usan el mismo estilo, y pulsar en su pista lleva la bolita a ese punto.
- Sin cambios en el proyecto ni en la base de datos.
- Pruebas: 66. Nueva: lo que guardan los paneles se lee igual que la línea del Director. El validador de XAML revisa también los eventos nuevos de la línea de tiempo y la plantilla del deslizador. La App compila contra los stubs de WPF. Pendiente en Windows: `dotnet build` y verlo.

## v1.1.0 — Interfaz: tema rojo y blanco claro/oscuro, y espacio para el Director y la Preview
- Tema propio con los colores de Loquendo, en claro y en oscuro. Se elige arriba a la derecha: «Sistema» sigue el modo de Windows (y cambia si lo cambias en Windows), o «Claro» / «Oscuro». Se aplica al momento, sin reiniciar, y se recuerda. En oscuro la barra de título de la ventana también es oscura (Windows 10 2004 o posterior).
  - Cabecera roja con la marca, el proyecto y las acciones. Pestañas con la seleccionada en rojo y texto blanco, sobre una franja con línea roja.
  - Tablas con cabecera roja, filas que alternan blanco y rojo muy suave (en oscuro, gris oscuro y rojo oscuro), resaltado al pasar el ratón y selección en rojo. Siguen el orden y el ancho de columnas de siempre.
  - Barras de desplazamiento finas y redondeadas que se ponen rojas al pasar el ratón.
  - Botones, campos, combos, casillas, listas, menús contextuales y tooltips con el mismo estilo. Los botones principales (Crear/Buscar/Guardar bloque/Generar voces/Reproducir/Exportar a VEGAS/Preparar borrador/Generar con IA/Aplicar) van en rojo.
  - Las ventanas secundarias (Configurar IA, elegir recurso, fuentes, etiquetas, prompt maestro…) usan el mismo tema.
- Guion, más espacio para trabajar:
  - Episodios y Escenas van apilados en una columna en lugar de dos. Esa columna se ensancha o estrecha con un divisor, y entre episodios y escenas hay otro divisor.
  - La tabla de bloques y el panel de abajo (Editor, Preview, Voces grabadas, Director, Director IA) se reparten con un divisor arrastrable. Antes el panel tenía una altura fija de 360 px; ahora ocupa por defecto 2/3 del alto.
  - «▲ Ampliar panel» (junto a «+ Diálogo») oculta episodios, escenas y la tabla de bloques, y deja el panel a pantalla completa. «▼ Restaurar» lo devuelve.
  - Dentro del Director (prompt) y del Director IA, un divisor reparte el espacio entre el prompt y la tabla del borrador. Antes el prompt tenía una altura fija.
  - Los tooltips largos se parten en varias líneas en lugar de salirse de la pantalla.
  - Sin los recuadros duplicados («Editor del bloque» dentro de la pestaña «Editor del bloque», etc.). La preview se ve sobre fondo negro.
- Biblioteca: un divisor reparte el espacio entre la tabla de fuentes y la de assets (antes, fuentes con 190 px fijos).
- Se recuerdan entre sesiones el tamaño de la ventana (o si estaba maximizada), el ancho de episodios/escenas y las proporciones de los divisores. Se guardan en `%LOCALAPPDATA%\LoquendoAI\ui.json`; si se borra, vuelve a lo predeterminado.
- Inicio con los pasos del flujo (proyecto, biblioteca, Voice Lab, guion, Director, VEGAS). La barra de estado de abajo ya no muestra un texto de la 0.0.8.
- Sin cambios en el proyecto ni en la base de datos. Los nombres de controles y eventos son los mismos, así que el comportamiento no cambia.
- Verificado en Linux:
  - Un validador de XAML propio (no se puede compilar WPF aquí) revisa tipos y propiedades de cada control, manejadores de eventos, recursos, nombres de plantilla, índices de filas y columnas, y divisores. Está comprobado con 10 errores provocados a propósito.
  - La App compila sin errores contra los stubs de WPF.
  - Pendiente en Windows: `dotnet build` y verlo.

## v1.0.1 — Cierre: efectos propios de VEGAS, desenfoque vertical en la preview y barras sin cortes
- Corregido: el desenfoque no hacía nada en VEGAS si estaba instalado Boris BCC. El script buscaba el efecto por un trozo del nombre («gauss») y se quedaba con el primero, «BCC Gaussian Blur», cuyos parámetros no son los del «Desenfoque gaussiano» de VEGAS. Ahora el Desenfoque gaussiano y el Cortador de galletas son siempre los efectos OFX propios de VEGAS: se eligen por su ID ({Svfx:com.vegascreativesoftware:…}; sonycreativesoftware en VEGAS 12/13) o, si una instalación les diera otro ID, solo por su nombre exacto. Nunca se toma un efecto de otro paquete (BCC, Sapphire, NewBlue…); si el de VEGAS no está, el aviso final lo dice.
- Corregido: en la preview el desenfoque solo desenfocaba de lado. FFmpeg copia la fuerza horizontal a la vertical solo al arrancar, y los cambios por fotograma solo movían la horizontal. Ahora se mueven las dos, como en VEGAS.
- Corregido: las barras de cine partían los eventos de VEGAS donde entraban, terminaban de moverse o salían; si eso caía durante un [TRANSICION] cruce, el fundido quedaba partido y no coincidía con la preview. Ahora las barras no cortan ningún evento: el Cortador lleva keyframes de Borde y Tamaño. Mientras no hay barras está en el tamaño que las quita (1,0 «cerrado», 0,747 «abierto»). Los saltos (barras de golpe, cambio de estilo) llevan una clave 1 ms antes. Los eventos solo se cortan donde corta la cámara.
- Corregido: un gesto partido por un corte de cámara (por ejemplo [CAMARA] habla con [GESTO] habla) rehacía la curva desde el corte en VEGAS, con una desviación de hasta ~0,6° y un quiebre. Ahora cada evento toma la curva exacta de la preview, fotograma a fotograma.
- `scripts\vegas\Diagnostico_efectos_14_o_superior.cs` (y `_12_13`) sustituye al diagnóstico del Cortador. Lista los efectos parecidos instalados (de VEGAS y de otros paquetes) con su ID, cuál elige Loquendo AI, y los parámetros y presets del Cortador y del Desenfoque gaussiano.
- Pruebas: 65. Nuevas: claves del Cortador sin cortes, con saltos y cambio de estilo; gesto partido por un corte de cámara; y el desenfoque vertical. Comprobado aparte que, con BCC instalado delante, el script generado elige el efecto de VEGAS y, con solo BCC, no elige ninguno y avisa.

## v1.0.0-beta.8 — Desenfoque (Desenfoque gaussiano de VEGAS)
- Nueva instrucción [DESENFOQUE]: desenfoca el fondo, un personaje, los personajes, las imágenes, los vídeos o todo, de golpe o poco a poco, y vuelve a enfocar. En VEGAS es el «Desenfoque gaussiano» en los eventos, con el rango horizontal y el vertical siempre iguales: «suavizar» 0,0200 (desenfoca más) y «ligero» 0,0100 (más suave); 0 es la imagen normal. Mientras cambia, los dos rangos llevan un keyframe por fotograma, a la vez. No se renderiza nada nuevo. La preview hace lo mismo.
  - `[DESENFOQUE] fondo | suavizar | duracion=800` — se desenfoca en 800 ms (0 = de golpe, por defecto; máximo 5000). También `ligero`, `valor=0,015` para un rango propio (hasta 0,1) y `estilo=ligero`.
  - `[DESENFOQUE] Bart | quitar | duracion=600` o `[ENFOCAR] Bart | duracion=600` — vuelve a enfocar. Así se hacen desenfoques y reenfoques dinámicos (enfocar a uno mientras se desenfoca el otro, simular que la cámara se acerca y enfoca…).
  - Objetivos: `fondo` (también los vídeos de fondo fijo), un personaje, `personajes`, `imagenes`, `videos` o `todos`. Es un estado del objetivo: un fondo o render que aparece después sale desenfocado igual, hasta otro [DESENFOQUE] que lo cambie. Termina con la escena.
  - Con lo demás: el desenfoque va después del Pan/Crop, así que la cámara no lo agranda (en VEGAS es así por naturaleza; la preview lo compensa) y los gestos se desenfocan con el render. Va antes del Cortador de galletas, así que las barras de cine siguen nítidas. El desenfoque de un render sale de su contorno sin cortarse.
- El Director IA puede usar el desenfoque (acción «desenfoque»: fondo, todos, personajes o un personaje; suavizar, ligero o quitar; 0–3000 ms) para centrar la atención, personajes lejos, recuerdos o sueños, o simular que la cámara enfoca.
- Editor de guion: tipo de bloque «Desenfoque» (con personaje, a él; sin personaje, al fondo); «Copiar escena» lo escribe como línea [DESENFOQUE].
- Pruebas: 63 (4 nuevas: lenguaje, nivel en el tiempo y claves por evento, acción de la IA, y render: se anima, no se corta en el borde, no crece con la cámara, y en VEGAS con claves y antes del Cortador).

## v1.0.0-beta.7 — Gestos de render: balanceo y rebote (técnica de Residents96)
- Corregido (tras la primera prueba en VEGAS): en VEGAS el rebote no aplastaba ni estiraba, el render se alejaba y volvía como un bumerán, y los gestos se veían más rígidos y cortados que en la preview. El evento tenía «Mantener relación de aspecto» activado (lo está por defecto), así que VEGAS encajaba el recuadro deformado en vez de estirar la imagen; ahora se desactiva en los eventos con rebote. Y en vez de 3 keyframes «Rápido», cuya curva e interpolación en VEGAS no son las de la preview, cada gesto lleva un keyframe lineal por fotograma con la misma curva que la preview.
- Nueva instrucción [GESTO]: el render se mueve como en los videos Loquendo hechos a mano, con el punto de eje del Pan/Crop en los pies y una curva tipo «Rápido» (arranque rápido, llegada suave). No se renderiza nada nuevo: en VEGAS son keyframes de Pan/Crop en el evento del render (uno por fotograma mientras dura el gesto), editables como siempre. La preview hace exactamente lo mismo.
  - `balanceo`: desde su inclinación actual, 4 fotogramas para inclinarse hacia un lado y 5 más para rebotar levemente al otro (40 % del ángulo), donde se queda; el siguiente balanceo parte de ahí.
  - `rebote`: se estira desde los pies en el fotograma 2 y vuelve en el 4 (con estirar negativo se aplasta). En VEGAS es el recuadro de Pan/Crop deformado, con «Estirar para llenar el fotograma» y sin «Mantener relación de aspecto», como desactivar «bloquear relación de aspecto» y «tamaño en centro».
  - Son independientes y se combinan: `balanceo+rebote` a la vez, `rebote, balanceo` uno y luego el otro, o líneas [GESTO] distintas en cualquier orden. Un gesto que empieza mientras otro sigue arranca desde la pose de ese momento. También funciona mientras el render se desplaza (animar x/y): los pies lo acompañan.
  - Opciones: `angulo=` 0,5–20° (5 por defecto), `estirar=` −30…40 % (8), `lado=` auto (hacia el centro de la pantalla y luego alternando), derecha o izquierda, `velocidad=` 0,5–3 o lento/rapido (1 = el tempo del tutorial a 25 fps), `eje=` pies, cintura o centro.
  - `[GESTO] habla | balanceo`: desde ahí, quien empieza cada línea hace el gesto (si está en pantalla), alternando el lado, hasta `[GESTO] habla | quitar`. También `[BALANCEO] Bart` y `[REBOTE] Bart`.
  - Un [GESTO] sobre un personaje que no está en pantalla es un error de la fila.
- El Director IA puede usar gestos (acción «gesto»: un personaje o «quien habla»; balanceo, rebote, ambos o quitar) antes de frases con energía.
- Editor de guion: tipo de bloque «Gesto (balanceo / rebote)»; «Copiar escena» lo escribe como línea [GESTO]. Las opciones se ajustan de momento con la línea del Director (prompt).
- Pruebas: 59 (5 nuevas de gestos: tiempos y curvas, reparto en escena con «habla», interrupción y secuencias, lenguaje, acción de la IA, y preview contra VEGAS —Pan/Crop nativo y medio normalizado— en reposo, inclinado y estirado, con los pies quietos, a ±3 px).

## v1.0.0-beta.6 — Encuadre cinematográfico: barras negras [CINE] (Cortador de galletas de VEGAS)
- Nueva instrucción [CINE]: barras negras arriba y abajo, animadas o de golpe, sobre todo lo que queda detrás. En VEGAS es el efecto «Cortador de galletas» en los eventos (a partir del preset «Cuadrado, centro, bordes blancos»: rectángulo, «cortar todo excepto la sección», color negro), con el Tamaño con keyframes mientras las barras se mueven; se puede retocar allí como siempre. No se renderiza nada nuevo. La preview dibuja las mismas barras.
  - `[CINE] mostrar | estilo=cerrado | duracion=800` — entran en 800 ms (0 = de golpe; por defecto 800, máximo 5000). Estilos: `cerrado` (barras anchas: Borde 1,0; Tamaño 1,0 → 0,748) y `abierto` (barras finas: Borde 0,560; Tamaño 0,747 → 0,639). También valen `abrir`, `ancho`, `fino`, `delgado`.
  - `[CINE] quitar | duracion=600` — salen (el Tamaño vuelve a su máximo). `cerrar` también vale.
  - `capas=todos` (por defecto: fondo, personajes, imágenes y vídeos), `capas=personajes` o `capas=Bart,Lisa`. Con personajes elegidos, el efecto tapa todo lo que está detrás de ellos; lo que esté delante (otro personaje, un prop) hereda el mismo efecto para no asomar por encima de las barras, y mientras ninguno de los elegidos está en pantalla no hay barras. Un nombre que no es un personaje registrado es un error de la fila.
  - Las barras siguen en las escenas siguientes hasta un [CINE] quitar: un episodio entero «en cine» es un solo [CINE] mostrar en la primera escena (la preview y la exportación de cada escena lo tienen en cuenta). Un [CINE] mostrar con otro estilo cambia al momento; con otras capas solo cambia qué capas llevan el efecto.
  - Con la cámara: las barras no se acercan, se quedan fijas en pantalla (en VEGAS el Cortador va después del Pan/Crop del evento; la preview hace lo mismo).
  - En VEGAS los eventos de las capas con barras se cortan donde las barras entran, terminan de moverse o salen. Si tu VEGAS no tiene el Cortador de galletas o sus parámetros se llaman distinto, la exportación sigue y el aviso final lo dice: `scripts\vegas\Diagnostico_cortador_galletas_14_o_superior.cs` (o `_12_13`) guarda en un .txt sus parámetros y presets para ajustarlo.
- Corregido (tras la primera prueba en VEGAS): el borde del Cortador de galletas quedaba blanco. El script buscaba el color como parámetro RGBA y en VEGAS es de otro tipo, así que no lo encontraba y no avisaba; ahora pone el negro sea cual sea el tipo del color (y si no pudiera, lo dice en el aviso final). El diagnóstico del Cortador muestra también el tipo y el valor del color.
- El Director IA puede usar las barras (acción «cine»: mostrar/quitar, cerrado/abierto, 0–3000 ms) solo cuando el encargo pide un momento épico, dramático, de película, flashback o suspenso.
- Editor de guion: tipo de bloque «Cine (barras negras)»; «Copiar escena» lo escribe como línea [CINE]. Estilo, duración y capas se ajustan de momento con la línea del Director (prompt); el panel propio llega con la ronda de UI/UX.
- Pruebas: 54 (6 nuevas de cine: lenguaje, plan de entrada/salida/interrupción/cambio de estilo, herencia entre escenas, capas elegidas y las que están delante, acción de la IA, y barras renderizadas: altura calibrada con tu captura de VEGAS, a mitad de la entrada, fijas con la cámara, y el efecto en los eventos correctos de VEGAS).

## v1.0.0-beta.5 — Cámara que sigue a los personajes, y Claude sin el error «compiled grammar is too large»
- Corregido: el Director IA con Claude fallaba con «400: The compiled grammar is too large…». Claude convierte el esquema de respuesta en una gramática con un límite de tamaño, y el nuestro llevaba todas las referencias del catálogo como listas cerradas (una rama «mostrar» por personaje, cada una con sus renders). Con Claude se usa ahora un esquema compacto (las referencias van como texto y la lista de renders por personaje va en el mensaje), y si aun así fuera demasiado grande, se reintenta una vez sin las listas largas. La app comprueba cada referencia de la respuesta (con todos los proveedores): un render de otro personaje, un fondo usado como SFX o una referencia inventada quedan como fila «pendiente» con el motivo, en vez de colarse.
- Nueva instrucción [CAMARA]: acerca TODO el cuadro (fondo, personajes, props) y queda editable en VEGAS: no se renderiza nada nuevo, son keyframes de Pan/Crop en los eventos que ya existen (los cortes de cámara parten el evento). La preview usa exactamente el mismo recorrido. Formas:
  - `[CAMARA] Bart | zoom=1.5 | duracion=300 | enfoque=cara` — encuadra a Bart. Si cambia de render o de posición, la cámara lo acompaña; si se mueve (animar), lo sigue; si sale de escena, vuelve al plano general, y si vuelve a entrar antes del siguiente [CAMARA], vuelve a él.
  - `[CAMARA] habla | zoom=1.3` — en cada diálogo encuadra a quien habla (si está en pantalla; si no, se queda donde está).
  - `[CAMARA] centro | zoom=1.3 | x=100 | y=-50` — un punto del cuadro. `[CAMARA] general | duracion=400` — vuelve al plano completo.
  - Opciones: zoom 1–3 (también 140 % o 1,4), duracion 0–5000 ms (0 = corte seco; por defecto 300), enfoque cara/cuerpo, x/y para ajustar el centro. La ventana nunca se sale del cuadro.
  - Dura hasta el siguiente [CAMARA] o el final de la escena. Una [CAMARA] sobre alguien que aún no está en pantalla avisa (irá a él cuando aparezca) y es un error si no aparece antes de la siguiente [CAMARA].
  - En una escena con bloques de cámara, el acercamiento automático de «primer plano» se desactiva para que mande la cámara.
- El Director IA también puede usar la cámara (acción «camara»: un personaje en pantalla, «quien habla» o «general»; zoom 110–250 %; 0–3000 ms; cara/cuerpo) con criterios de uso: 1 a 4 momentos por escena (reacciones, sorpresas, remates), y volver a general después.
- Editor de guion: tipo de bloque «Cámara» (con personaje encuadra y sigue; sin personaje vuelve al plano general). El zoom, la duración y el enfoque se ajustan de momento con la línea [CAMARA] del Director (prompt); un panel propio llega con la ronda de UI/UX. «Copiar escena» escribe los bloques de cámara como líneas [CAMARA].
- El «primer plano» automático ya no se hornea en los medios normalizados de VEGAS: también es Pan/Crop. Una imagen fija normalizada se renderiza una sola vez y se reutiliza en todos los tramos, escenas y bloques que la muestran.
- Pruebas: 48 (7 nuevas de cámara: lenguaje, regla de «en pantalla», recorrido, seguimiento, «quien habla», acción de la IA y comparación renderizada preview/VEGAS quieta, a mitad del movimiento y con el primer plano automático).

## v1.0.0-beta.4 — Pruebas automáticas, base de datos v7, una sola geometría y parámetros tipados
- Corregido: «[MOSTRAR] Bart | feliz» podía tomar el render «feliz» de OTRO personaje (o cualquier recurso que empezara por «feli») cuando Bart no tenía uno con ese nombre. Ahora busca solo entre los renders de Bart (su carpeta o su personaje en la regla); si no hay ninguno con ese nombre usa el más parecido de Bart y lo marca con ⚠ en el estado de la fila; nunca uno de otro personaje.
- Corregido (venía de beta.3): reescanear una fuente después de renombrar o mover un archivo, o de cambiar el personaje o la colección de una carpeta, terminaba con avisos «UNIQUE constraint failed: asset_fts_map.asset_id» y ese recurso no se actualizaba. Los triggers del índice de búsqueda usaban «INSERT OR IGNORE», que SQLite anula cuando la escritura es un UPSERT (como la del escaneo). La base v7 los sustituye; si viste esos avisos, reescanea esas fuentes una vez.
- Una sola geometría: la preview, el Pan/Crop nativo de VEGAS y los medios normalizados calculan la posición de cada capa con el mismo código (LayerGeometry). Antes cada uno lo hacía por su lado: un render girado podía quedar hasta 141 px desplazado en VEGAS (giraba alrededor de otro punto), y un fondo con caja menor que la pantalla ocupaba toda la pantalla en VEGAS. Ahora todo coincide a ±1 px; el giro en VEGAS es alrededor del centro de la capa, como en la preview.
- Parámetros de bloque tipados: el JSON de cada bloque se interpreta en un solo sitio (BlockParameters), una vez por texto, con los valores por defecto definidos una sola vez (antes se leía en 29 sitios con defaults repetidos). Las claves que esta versión no conoce se conservan al guardar.
- Base de datos v7 (con copia antes de migrar): se eliminan las tablas que nunca se usaron (scene_characters, dialogue_lines, effects, jobs; los diálogos antiguos ya se habían pasado a bloques) y la copia de la versión que había en project_info. La versión vive en la base (schema_info) y el código la define en un solo sitio; project.loquendo.json la refleja para que una versión anterior rechace el proyecto en vez de abrirlo. Un proyecto de una versión más nueva se rechaza sin tocarlo. Sin FTS5, la búsqueda sigue con LIKE y el índice se reintenta al abrir.
- La interfaz ya no se congela con bibliotecas grandes: las lecturas del catálogo (biblioteca, selector de recursos, Director, limpieza, análisis de imágenes) van en conexiones de solo lectura en segundo plano (Microsoft.Data.Sqlite ejecuta sus métodos «Async» de forma síncrona). El refresco de la biblioteca ya no abre ni revisa la base otra vez, y «Quitar fuente» borra en segundo plano.
- Proyecto de pruebas `tests\LoquendoAI.Tests` (en la solución, sin paquetes extra): 41 pruebas de reparto de tomas, esquema del Director y su lenguaje, continuidad, parámetros, geometría (preview contra VEGAS renderizando de verdad), migraciones de proyectos v1 y beta.3, rechazo de proyectos nuevos, escaneo con SQLite real, búsqueda, limpieza, copias y exportación de episodio a VEGAS. `scripts\pruebas.cmd` las ejecuta; las que necesitan FFmpeg se omiten si no está.
- Para poder probarla, la lógica del Director sin interfaz pasó de la ventana a `LoquendoAI.Infrastructure/Director` (DirectorScript, DirectorAiSchema, EpisodePlanning, AudioDurationEstimate). Sin cambios de comportamiento.
- Se quitaron modelos y contratos sin uso (JobRecord, DialogueLine, EffectIntent, CharacterPlacement, interfaces de proveedores sin implementar) y cinco métodos del repositorio que nadie llamaba.
- Errores en segundo plano (tareas no esperadas, hilos de trabajo) se anotan ahora en el mismo registro de errores en lugar de perderse.
- Documentación: `docs/ARCHITECTURE.md` describe el reparto actual del código y la base de datos.
- Verificado en Linux: las 41 pruebas pasan con SQLite real (3.45, FTS5) y FFmpeg; la comparación de geometría (7 casos × Normal/Legacy) queda en ≤1 px; compilación de App con los stubs de WPF sin errores. Pendiente en Windows: `dotnet build`, `scripts\pruebas.cmd` con Microsoft.Data.Sqlite, la app y VEGAS.

## v1.0.0-beta.3 — Todos los proveedores a la vez (con Claude), búsqueda FTS5 y STT mejorado
- IA sin «proveedor activo»: las listas de modelos del Director IA y de la Biblioteca muestran a la vez los modelos visibles de Ollama, Gemini y Claude («gemma4:12b · Ollama», «gemini-2.5-flash · Gemini», «claude-sonnet-5 · Claude») y cada petición va al proveedor del modelo elegido. Junto al modelo del Director aparece «Razonamiento» (Gemini 3) o «Esfuerzo» (Claude, hasta «Máximo» si el modelo lo admite) solo cuando el modelo tiene esa opción; con los demás se oculta.
- «Configurar IA» rehecho: 1) Proveedores: Ollama con «Refrescar modelos»; Gemini y Claude con su API key («Guardar clave y consultar», «Borrar clave», también `GEMINI_API_KEY` / `ANTHROPIC_API_KEY`). 2) Modelos en las listas: casillas por modelo con búsqueda, «Marcar todos», «Ninguno» y «Recomendados»; los modelos nuevos de Ollama aparecen marcados y de Gemini solo los principales (sin TTS, imagen, embeddings, versiones fechadas…). 3) Predeterminados: modelo del Director, modelo de imágenes, razonamiento de Gemini 3 y esfuerzo de Claude. La ventana ya no se abre en cada inicio: solo la primera vez. Elegir otro modelo en la pestaña vale para la sesión; los predeterminados se cambian aquí. La configuración anterior (un proveedor) se migra sola.
- Claude como proveedor (API de Anthropic): salida estructurada con el mismo esquema tipado que Ollama y Gemini (los rangos numéricos, que la API no admite, pasan a la descripción; la app ya los limita), imágenes para la Biblioteca, esfuerzo por modelo según su catálogo, reintentos ante 429/5xx/529 respetando `retry-after`, mensajes claros para `max_tokens` y `refusal`, y su línea en `ia-diagnostico.log` (tokens de entrada/salida, esfuerzo, intentos). La clave se guarda cifrada como la de Gemini y nunca aparece en el log.
- Búsqueda de la biblioteca con índice FTS5 (base de datos v6, con copia de seguridad antes de migrar): cada palabra se busca como inicio de palabra, sin importar tildes ni mayúsculas, en nombre, ruta y etiquetas del Director (descripción, expresión, uso, sujeto), ordenado por relevancia (el nombre pesa más). «cancion epica» encuentra «Canción Épica.mp3»; «bart eno» encuentra «Bart/Enojado/…». Lo usan la Biblioteca (sumado a la búsqueda de siempre, así que nada deja de encontrarse) y «Elegir recurso de fila…». El índice se mantiene solo (triggers) y un reescaneo sin cambios no lo reescribe. Si esa instalación de SQLite no tuviera FTS5, el proyecto abre igual y se usa la búsqueda anterior.
- Voces grabadas (STT): nuevo modelo «large-v3-turbo» (casi la calidad de large-v3, mucho más rápido); en GPU usa float16 (int8_float16 si la tarjeta no lo admite) y en CPU int8, antes int8 siempre (`LOQUENDO_AI_STT_COMPUTE` lo fuerza). «Nombres como pista» pasa los nombres de los personajes al reconocedor para que escriba «Fluttershy» o «Pinkie Pie» como en tu proyecto. Marcas de tiempo por palabra: la columna STT indica las palabras dudosas («STT · revisar (2 dudosas)») y su tooltip dónde empieza y termina la voz, el silencio sobrante y el equipo usado; al incorporar, los tiempos por palabra se guardan con la toma (para subtítulos, recorte de silencios o lip-sync).
- Robustez: si la carpeta `%LOCALAPPDATA%` de la app no existiera, se crea en lugar de escribir la configuración en una ruta relativa.
- Verificado en Linux: cliente de Claude contra una API simulada (paginación y capacidades de `/v1/models`, cuerpo con `output_config.format` + `effort`, imagen, bloque de thinking ignorado, 529×2, 429 con retry-after, 429 de gasto sin reintento, `max_tokens`, `refusal`, 401 sin exponer la clave); esquema del Director saneado para Claude; configuración: claves «proveedor|modelo» (con «:» de Ollama), migración desde beta.2, modelos visibles por defecto y guardado; migración v6 aplicada sobre el esquema real con SQLite: búsqueda en etiquetas, sin tildes, por prefijo, triggers de alta/cambio/borrado de assets y etiquetas, reescaneo sin reescritura, `integrity-check`, 50 000 assets (0,4 s de migración; mismos resultados que LIKE) y la consulta del selector con sus filtros; worker STT con un faster-whisper simulado (GPU float16, sin CUDA → CPU int8, GPU sin float16 → int8_float16, pista y palabras) y confirmado en faster-whisper 1.2.1 que existen `large-v3-turbo`, `initial_prompt` y `word_timestamps`. Regresiones de beta.1/beta.2 y hotfix 17–22 en verde; la App compila con stubs de WPF sin errores en lo cambiado. Pendiente en Windows: `dotnet build`, la ventana nueva, Claude/Gemini reales y el STT real.

## v1.0.0-beta.2 — Selector de recursos fiable, continuidad de episodio, Gemini con reintentos y copias de seguridad
- Corregido «Elegir recurso de fila…» en el Director (prompt) y el Director IA: a veces el recurso elegido no quedaba en la fila (el error seguía hasta escribir a mano el nombre o la ruta) y, tras editar una fila con opciones (`[IMAGEN] … | duracion=…`), la siguiente elección podía ir a parar a otra fila. Causa: las filas del borrador se comparaban por contenido, así que al editar la instrucción cambiaba su identidad y la tabla (compartida por las dos pestañas) perdía la fila seleccionada. Ahora cada fila es única, la celda se actualiza al momento sin recargar la tabla y, si el borrador cambió mientras elegías, lo dice en vez de no hacer nada. El mismo problema latente en la columna «Descripción IA (editable)» de la Biblioteca también queda corregido.
- Director de episodio, continuidad: al dirigir cada escena, la IA recibe cómo terminó la anterior (fondo, qué personajes quedaron en pantalla con qué render y en qué posición, y la música) con referencias de su propio catálogo, más si la escena ocurre en el mismo lugar o cambia. Esos recursos se incluyen siempre en el catálogo y el render mostrado sigue contando como de ese personaje aunque no esté etiquetado.
- Transiciones VEGAS: si la premisa habla de negro/oscuridad/apagón (o blanco/desmayo), la IA recibe también «Disolvente · Desvanecimiento en negro/blanco» de VEGAS. A mano: `[TRANSICION] cruce | duracion=800 | vegas={Svfx:com.vegascreativesoftware:dissolve} | preset=Desvanecimiento en negro`.
- Gemini: reintenta automáticamente (hasta 4 intentos) ante 429 (límite de peticiones), 500/502/503/504 (modelo saturado) y cortes de conexión, respetando la espera que indica Google (Retry-After o RetryInfo); si Google pide esperar más de 60 s (cuota diaria agotada) no reintenta y lo explica. El estado del Director muestra «Gemini: servicio ocupado (503). Reintento 2/4 en 6 s…». Si Gemini corta el JSON por longitud (MAX_TOKENS) lo dice en vez de un error de JSON. Cada llamada deja su línea en `ia-diagnostico.log` (resultado, segundos, intentos, tokens de prompt/salida/razonamiento, motivo de fin); la clave nunca aparece en el log.
- Copias de seguridad de `project.db` en `<proyecto>\backups`: una automática al día al abrir el proyecto (en segundo plano), una antes de cada actualización de la base de datos y el botón «Copia de seguridad» (arriba, junto a Abrir proyecto). Se hacen con `VACUUM INTO`: copia coherente que incluye los cambios aún en el WAL, sin cerrar el proyecto; se guardan las 10 últimas de cada tipo. Para restaurar: cierra la app, renombra `project.db` (y `-wal`/`-shm`) y copia la copia como `project.db`.
- «Limpiar caché…» (junto a Copia de seguridad): muestra cuánto ocupa y pide confirmación antes de borrar previews antiguas (se conserva la más reciente de cada escena y la que estás viendo), voces TTS que ya no usa ningún bloque de ninguna escena, caché de voces sin uso de más de 30 días y temporales interrumpidos. Nunca toca recursos, voces grabadas, exportaciones a VEGAS (solo informa su tamaño: un .veg guardado puede usarlas), exportaciones ni copias. También descarta mediciones de archivos que ya no existen.
- Verificado en Linux: equivalencia de las filas y copia sin suscriptores; estado final de escena y línea de continuidad (último fondo, personajes que salen, JSON dañado); Gemini contra un servidor simulado (503×2 → éxito, 429 con RetryInfo y con Retry-After, cuota de 3600 s sin reintentos, 503 persistente → 4 intentos, 400 sin reintento, conexión cortada, MAX_TOKENS, cancelación durante la espera, clave ausente del log); `VACUUM INTO` desde una conexión de solo lectura copia datos que aún están en el WAL mientras otra conexión escribe; plan de limpieza exacto sobre un proyecto de prueba y rechazo de rutas fuera de `generated`, de voces grabadas y de VEGAS; rotación de copias. Regresiones de beta.1 y hotfix 17–22 en verde; la App compila con stubs de WPF sin errores nuevos. Pendiente en Windows: `dotnet build` y probar el selector en la tabla real.

## v1.0.0-beta.1 — Transiciones de VEGAS en el Director IA, voces más rápidas y caché de mediciones
- Director IA: además de corte/fundido/disolvente/flash/barrido, cada borrador recibe hasta 12 transiciones de VEGAS de tu instalación (T1, T2…: NewBlue, Sapphire, Boris, VEGAS…) y el esquema solo le deja elegir esas. En una transición «cruce» se convierten en su efecto VEGAS (`vegas=<ID> | preset=<preset>`), y en el fondo, render o imagen que entra con ese cruce en `transicion=plugin | vegas=<ID> | preset=<preset>` (como siempre, la transición de un visual solo actúa tras un cruce; el prompt se lo explica a la IA). La lista cambia en cada generación: primero tus favoritas, después las que encajan con palabras de la premisa (zoom, explota, destello, página, gira, cristal, fuego…, como máximo la mitad), luego transiciones conocidas y el resto al azar. El prompt base le pide usarlas solo en 1–3 momentos clave. En la preview se ven como un fundido; el efecto real se aplica al abrir la exportación en VEGAS, como con las elegidas a mano.
- Director IA → «Transiciones VEGAS…»: importa el `transiciones_vegas.txt` que genera `scripts/vegas/Listar_transiciones_14_o_superior.cs` (o `_12_13`). Se guarda en `%LOCALAPPDATA%\LoquendoAI\vegas_transiciones.json` y se combina con el catálogo integrado (gana tu instalación); el editor de bloques también lo usa. Favoritas opcionales en `%LOCALAPPDATA%\LoquendoAI\vegas_transiciones_favoritas.txt`, una por línea: `Nombre o ID | preset` (el preset es opcional; `#` comenta).
- Generar voces: las líneas que faltan se sintetizan 2 a la vez (cada una en su propio proceso aislado del bridge, igual que antes) y se guardan en la caché de voces; después el pase normal, en orden, las toma de la caché, calcula tiempos y guarda la escena como siempre. Si 2 líneas fallan en paralelo, el resto se hace una por una y los errores se informan como antes. `LOQUENDO_AI_TTS_PARALLEL=1` vuelve al modo anterior (máximo 6). También aplica a «Exportar episodio».
- TTS: una síntesis que no responde ya no bloquea la generación para siempre: se detiene tras 60 s + 1 s por cada 12 caracteres (máx. 10 min), cierra el bridge y su proceso Loquendo, y muestra el error. `LOQUENDO_AI_TTS_TIMEOUT_SECONDS` fija otro límite (0 lo desactiva). Cada generación deja en `ia-diagnostico.log` cuántas voces se generaron/reutilizaron y cuánto tardó.
- Caché de mediciones: la duración de los WAV PCM/float se lee de la cabecera, sin lanzar ffprobe. Duraciones de MP3/OGG/vídeo, pistas de audio en vídeo, dimensiones, alfa, zona visible de renders y bordes de fondos se recuerdan en `%LOCALAPPDATA%\LoquendoAI\probe-cache.json` por ruta + tamaño + fecha de modificación (si cambias el archivo se mide otra vez). La preview, «Exportar a VEGAS», el selector de recursos y «Generar voces» dejan de medir lo mismo en cada pasada. `LOQUENDO_AI_PROBE_CACHE=0` lo desactiva.
- Versión 1.0.0-beta.1 en el título y en los ensamblados.
- Verificado en Linux: el `transiciones_vegas.txt` real (1450 entradas, 363 transiciones únicas, 5560 presets) se importa y deduplica; cada opción propuesta resuelve ID + preset en el catálogo; 8 acciones tipadas con T-refs pasan por el parser real del Director sin errores; la gramática GBNF de llama.cpp acepta T1…T5 y rechaza una T inexistente o un nombre libre; síntesis en paralelo con un bridge simulado (6 líneas únicas, 2 a la vez, duplicados/importadas fuera, segunda vez todo desde caché, parada tras 2 fallos, 3 a la vez con la variable, cancelación y timeout matando el árbol de procesos); duración por cabecera igual a ffprobe en WAV 16 bits, float, 24 bits extensible y 50 ms, ADPCM y MP3 siguen por ffprobe; caché persistente leída por un segundo proceso e invalidada al reemplazar el archivo; regresiones de hotfix 17–22 (escaneo, render de 90 capas, exportación de episodio en ambos modos de audio, esquema tipado, detección por nombre de archivo, reparto de episodio) en verde. La App compila con stubs de WPF sin errores nuevos. Pendiente en Windows: `dotnet build`, Ollama real, Loquendo en paralelo y abrir el script en VEGAS con un plugin elegido por la IA.

## v0.9.0-alpha hotfix 22 — Tomas grabadas: quién habla y voz TTS separados
- Regenerar con TTS una toma grabada usa SOLO la voz asignada a la toma («Asignar voz» en Voces grabadas o «Voz» en el editor: perfil o voz TTS7 directa). La voz por defecto del personaje en Voice Lab ya no interviene; sin voz propia, el botón explica cómo asignarla. La pregunta del editor al cambiar una toma también aparece solo si la toma tiene voz propia.
- Voces grabadas: columnas «Tipo» (Diálogo / Narración, se deriva de quién habla: Narrador = narración), «Quién habla» y «Voz TTS (opcional)». Los tooltips explican qué hace cada una.
- Guion: en «Personaje / asset», una toma grabada muestra quién habla y, si la tiene, su voz TTS propia («Bart (TTS: Jorge grave)»). Antes mostraba el perfil heredado del personaje o nada.
- Sin cambios de datos: las tomas ya importadas conservan personaje, voz, WAV y transcripción.

---

## v0.9.0-alpha hotfix 21 — Voces grabadas a prueba de accidentes
- Qué hace asignar un personaje a una toma: es una ETIQUETA (quién habla para el Director, pista por personaje en VEGAS, Diálogo en vez de Narración). Aunque ese personaje tenga voz en Voice Lab, «Generar voces de escena», la preview, «Exportar a VEGAS» y «Exportar episodio» usan siempre el WAV grabado; nunca lo sustituyen por TTS.
- «Regenerar bloque con TTS» pide confirmación indicando el perfil que usará y guarda la referencia a la grabación en el bloque. «Restaurar grabación» vuelve a la toma original en cualquier momento. Si la generación falla o se cancela, el bloque recupera la grabación.
- Editor de guion: si cambias el texto, el personaje o la voz de una toma grabada y existe un perfil TTS utilizable, pregunta si quieres regenerarla (por defecto No: solo guarda los cambios y conserva el WAV).
- Corregido: cambiar una toma de un personaje a Narrador (o al revés) en el editor borraba la referencia al WAV grabado sin avisar. Ahora la conserva; también se permite dejar sin texto una toma grabada al cambiar su tipo de voz.
- Aplicar un borrador con «Sustituir bloques actuales» sobre una escena con voces grabadas avisa de cuántas dejará de usar (los WAV siguen en el proyecto) antes de continuar.
- Las tomas regeneradas con TTS que conservan su grabación siguen contando como voces grabadas para el Director (modo voces grabadas) y el Director de episodio.
- El tooltip de «Asignar voz» del importador aclara que es opcional y solo se usa si luego regeneras con TTS.

---

## v0.9.0-alpha hotfix 20 — Director de episodio y exportación de episodio a VEGAS
- Director IA → «Generar episodio (varias escenas)». Historia: la IA planifica el episodio (1–12 escenas con título, lugar, personajes y resumen), pide confirmación y dirige cada escena con el mismo Director tipado, pasándole el esquema del episodio y cómo terminó la escena anterior. Voces grabadas: reparte las tomas de la escena seleccionada en escenas consecutivas (la IA elige los cortes; máximo 40 tomas por escena y 12 escenas, con partes iguales si la IA falla) y monta cada una.
- Las escenas se crean NUEVAS al final del episodio en cuanto cada una está lista: nada existente se modifica y, si cancelas, se conservan las terminadas. Las filas que no pasan la validación quedan como comentario «⚠ Revisar: instrucción — motivo»; las tomas que la IA olvide se añaden al final con aviso, así que ningún WAV se pierde. La escena con las tomas originales no se toca.
- «Exportar episodio» (junto a Exportar a VEGAS): todas las escenas del episodio seleccionado en UN proyecto VEGAS, una tras otra, con una región con nombre por escena. Las pistas con la misma clave (fondo, cada personaje en pantalla, cada voz) continúan entre escenas. Genera antes las voces TTS que falten, escena por escena.
- Nuevo selector «Audio: por personaje / Legacy» para la escena y el episodio. Por personaje: una pista «Voz · Bart», «Voz · Narrador»… en todo el proyecto (una pista «(2)» solo si dos líneas del mismo personaje se solapan); música, SFX y audio de vídeo se agrupan en el menor número de pistas con el mismo volumen y sin eventos solapados, para que VEGAS no haga fundidos automáticos. Legacy: cada audio en su pista, como antes.
- Verificado en Linux: episodio de 2 escenas exportado con FFmpeg real en ambos modos (6 pistas por personaje frente a 14 en Legacy, tiempos desplazados a la escena 2, regiones correctas); los scripts .cs generados para VEGAS 14+ y 12/13 compilan contra una API de VEGAS simulada; reparto de tomas (100, 50, 60 y 500 tomas, cortes de la IA, límites) y montaje de escena (filas rechazadas, tomas olvidadas) con pruebas; esquemas del plan compilados a gramática con llama.cpp. La App compila con stubs de WPF sin errores nuevos. Pendiente en Windows: `dotnet build`, Ollama real y abrir el script en VEGAS (la creación de regiones usa `new Region(posición, duración, nombre)`).

---

## v0.9.0-alpha hotfix 19 — Personajes como etiqueta en Voces grabadas
- «Voces grabadas»: el selector de personaje es editable. Elige uno o escribe un nombre nuevo y pulsa «Asignar personaje»: si no existe se crea como personaje SIN voz TTS (solo etiqueta), sin pasar por Voice Lab. Las tomas grabadas nunca necesitaron perfil de voz; Voice Lab puede añadírselo después si quieres regenerar alguna línea con TTS.
- Al añadir WAV, el personaje se reconoce por el nombre del archivo cuando coincide con uno registrado (`03_Bart_hola.wav`, `bart01.wav`, `Pinkie Pie - toma 2.wav`), sin distinguir mayúsculas ni tildes. Si el nombre menciona a dos personajes distintos, o solo contiene palabras sueltas como «al», no adivina. El estado indica cuántas tomas se reconocieron.
- Director IA en modo voces grabadas: la lista de personajes indica quién habla en las tomas (en vez de «voz sin perfil») y esos personajes reciben el mismo abanico amplio de renders que un personaje nombrado en la premisa.

---

## v0.9.0-alpha hotfix 18 — Director tipado, selector de catálogo y variedad de renders
- Director IA con salida tipada: la IA ya no escribe líneas `[FONDO] A1 | …`. Cada paso es un objeto de acción (`fondo`, `mostrar`, `ocultar`, `dialogo`, `imagen`, `video`, `musica`, `sfx`, `pausa`, `transicion`, `conservar`) con listas cerradas y rangos. El esquema limita cada acción a los recursos que le sirven y a cada personaje a SUS renders, así que la IA no puede elegir un fondo como SFX, el render de otro personaje ni inventar referencias o encuadres. La app convierte cada acción a la instrucción de siempre, así que la tabla, «Validar edición», «Copiar borrador» y el Director (prompt) no cambian.
- Prompt maestro: la base nueva solo tiene criterios de dirección (~1,5 k caracteres frente a ~4 k): la sintaxis la impone el esquema. Si tu proyecto guardó un prompt maestro con la sintaxis antigua, se sigue usando con una nota de formato y el editor sugiere «Restablecer base». En voces grabadas, los bloques originales se citan como B1, B2… en vez de GUID (menos tokens y menos errores).
- Variedad de renders: el catálogo enviado se arma por uso (renders, fondos, imágenes, vídeo, música, SFX). En cada grupo van primero los que coinciden con la premisa y el resto se sortea en cada generación; antes siempre ganaban los ya etiquetados. Personajes mencionados reciben hasta 10 renders. Cada recurso lleva también su carpeta (p. ej. `Bart/Enojado`), útil cuando aún no está etiquetado.
- Música y SFX separados para la IA: solo recibe como música lo catalogado como Música y como efecto lo catalogado como SFX. Un audio «Sin tipo» entra según su duración estimada por tamaño (≤ 10 s seguro → efecto; ≥ 60 s seguro → música; si es dudoso no se ofrece). Un «SFX» de varios minutos o una «música» de 2 s tampoco se ofrecen, y si la IA cruza los tipos la fila queda pendiente con el motivo. El catálogo enviado indica la duración aproximada de cada audio. Cuando eliges tú a mano, cualquier audio sirve para ambos (antes esas filas no se resolvían).
- «Elegir recurso de fila…» abre un selector de la biblioteca en vez del explorador de Windows: solo archivos que esa fila puede usar, búsqueda por nombre, carpeta y etiquetas IA/manuales, filtro por fuente, «solo catalogados como…» (desmárcalo para ver todo el audio/imágenes), «solo de <personaje>» en renders, miniatura y botón Escuchar para audio; en audio muestra la duración aproximada y, al seleccionar, la exacta con aviso si un SFX parece música o una música es demasiado corta.
- La columna Personaje/recurso del borrador muestra «Bart · nombre_del_render».
- Corrección de hotfix 17: un recurso usado en un guion y oculto por «Ignorar» seguía sin resolverse en la preview; ahora se usa mientras el archivo exista.
- `LOQUENDO_AI_DIRECTOR_LEGACY=1` vuelve al formato de líneas si alguna versión de Ollama no acepta el esquema.
- Verificado en Linux: 20 acciones tipadas convertidas y analizadas con el parser real del Director sin errores; el esquema compila a gramática GBNF con el conversor de llama.cpp (el que usa Ollama) y la gramática acepta escenas válidas y rechaza SFX con fondo, render ajeno, personaje inventado, referencia inexistente, encuadre «medio», pausa fuera de rango, formato de líneas y `conservar` en modo historia; la consulta del selector se ejecutó contra el esquema real de SQLite; regresiones de hotfix 17 en verde; la App compila con stubs de WPF sin errores nuevos. Falta `dotnet build` y prueba con Ollama real en Windows.

---

## v0.9.0-alpha hotfix 17 — Director IA sin cuelgues, caché de voces y catálogo sin pérdidas
- Director IA / Ollama: las respuestas llegan por streaming con límites explícitos (`num_ctx` 8k/16k/32k según el tamaño del encargo, `num_predict` 4096 en historia y 6144 en voces grabadas, `think=false`, `keep_alive=15m`). Antes Ollama usaba 4k de contexto en GPU < 24 GB, recortaba el encargo sin avisar y un modelo en bucle podía generar hasta el timeout de 8 minutos, que además se mostraba como «cancelado».
- Vigilancia de la respuesta: 5 min máximo hasta el primer token (carga del modelo + lectura), 90 s máximo sin tokens nuevos, corte inmediato si el JSON entra en un bucle de espacios o el modelo razona sin empezar. Errores claros para respuesta cortada por límite, JSON incompleto, conexión perdida o modelo inexistente. Cancelar sigue siendo cancelar. Ajustables con `LOQUENDO_AI_OLLAMA_FIRST_TOKEN_SECONDS`, `LOQUENDO_AI_OLLAMA_IDLE_SECONDS` y `LOQUENDO_AI_OLLAMA_NUM_CTX`; se respeta `OLLAMA_HOST`.
- El estado del Director muestra el progreso (cargando, razonando, tokens generados). Cada petición deja una línea en `%LOCALAPPDATA%\LoquendoAI\ia-diagnostico.log` con tiempos, tokens, contexto, motivo de fin, porcentaje del modelo en GPU y, si falla, la salida parcial.
- El catálogo del Director se lee en una conexión de solo lectura fuera del hilo de la interfaz (antes congelaba la ventana con bibliotecas grandes).
- Voces: caché por contenido en `generated/voices/_cache`. Una línea con la misma voz, prosodia y texto se reutiliza aunque el bloque sea nuevo (borradores IA regenerados, escenas copiadas); también encuentra WAV generados antes de esta versión.
- Biblioteca: un archivo movido o renombrado dentro de la misma fuente conserva su ID, etiquetas y referencias del guion (se detecta por tamaño + SHA-256). La regla «Ignorar» ya no borra recursos usados en guiones o con ediciones manuales: quedan ocultos. El resumen del escaneo muestra los movidos.
- PNG con paleta y transparencia `tRNS` (típicos de sprites extraídos) se reconocen con alfa en vez de marcarse «necesita recorte». Se reclasifican al reescanear.
- SQLite deja el modo de caché compartida (desaconsejado por SQLite; bloqueaba tablas completas frente a WAL).
- Preview: los grafos de FFmpeg muy largos se pasan por archivo (`-/filter_complex`, con respaldo `-filter_complex_script` en FFmpeg < 7) para no superar el límite de 32 767 caracteres de Windows en escenas largas.
- Verificado en Linux: Core e Infrastructure compilan con advertencias como error; el cliente de Ollama pasó 10 escenarios contra un servidor simulado (bucle de espacios, silencio, corte, límite, JSON roto, cancelación); el escaneo pasó pruebas de movimiento, ignorados protegidos y PNG tRNS; FFmpeg 6.1 renderizó una escena de 90 capas por archivo de grafo. La App WPF se verificó con stubs de WPF (sin errores nuevos); falta `dotnet build` real en Windows.

---

## v0.0.8-alpha hotfix 19 — volumen por recurso y audio de video

Añade `volumePercent` (entero 0–200) a SFX, Música y Video desde el editor y
el Director (`volumen=...`). Los valores antiguos se mantienen con sus
predeterminados: SFX 100%, música 25%, video 0%. `SceneComposer` mezcla cada
recurso con su ganancia propia; para video mayor que 0 detecta si hay pista
de audio y reutiliza el input visual, respetando duración y reemplazos. El
exportador de VEGAS crea pistas de audio independientes con la misma ganancia
y solo importa audio de videos que realmente lo tienen. El selector de voz y
Voice Lab no cambian. Las propiedades nuevas en JSON son opcionales; no se
requiere migración de base de datos.

Validación pendiente en Windows: `dotnet build`, preview con un video con
y otro sin pista de audio, inspección de pistas en VEGAS y contraste con
volumen 0, 15, 65, 100 y 150%. Este entorno no dispone del SDK .NET.

---

## v0.0.8-alpha hotfix 18 — tupla de transición sin nombres incompatibles

Soluciona los dos errores CS8123 informados por `dotnet build` en
`SceneComposer.cs(370)`: la rama sin transición devolvía una tupla literal con
nombres `Id` y `Preset`, pero el tipo inferido para la expresión condicional
era `(string, string)` sin esos nombres. Mantiene la tupla y sus valores;
el MP4 y la exportación a VEGAS utilizan los mismos efectos del hotfix 17.
Compilación de WPF pendiente de confirmar en Windows porque este entorno no
tiene el SDK de .NET.

---

## v0.0.8-alpha hotfix 17 — catálogo de transiciones por recurso

Partiendo de hotfix 16, se incluye una instantánea de 389 plugins distintos y
1542 presets inventariados en el VEGAS del usuario. El editor permite elegir
plugin y preset desde un único selector para un bloque Cruce de capas o para
el recurso visual entrante (fondo, render, imagen/GIF o video). El Director
admite `vegas=nombre o ID | preset=...` en `[TRANSICION] cruce` y
`transicion=nombre o ID | preset=...` en recursos visuales. Admite el alias
`transicion=plugin | vegas=...` y valida nombres, IDs y presets del catálogo.

El plan del compositor conserva ID y preset por medio entrante y su duración de
solapamiento. El script exportado asigna la transición a su FadeIn y avisa si
el plugin no existe en la instalación activa. MP4 conserva el fundido como
aproximación; NewBlue, Sapphire y otros plugins propietarios se renderizan en
VEGAS. Los tres efectos integrados (disolvente, flash, barrido) permanecen.
Confirmar `dotnet build` y la ejecución de presets en Windows con VEGAS.

---

## v0.0.8-alpha hotfix 16 — paridad de video y SFX en Director

Partiendo de hotfix 15, el Director reconoce `esperar=si/no` para SFX además
de `esperar`; `duracion=ms` para audio y video; `pausa=ms` tras cualquier
bloque etiquetado; las etiquetas de capas de video del editor, `pantalla verde=si/no`,
`mover x/y` y los controles visuales existentes. Las opciones llegan a
`SceneScriptBlock` y a las mismas propiedades que lee `SceneComposer`. El
borrador muestra las opciones elegidas. El SFX que espera desplaza el reloj;
un SFX con `esperar=no` puede continuar bajo el diálogo.

No se cambia el formato de proyecto ni la selección flexible de recursos
agregada en hotfix 15. La validación de compilación requiere `dotnet` en
Windows; este entorno no dispone del SDK.

---

## v0.0.8-alpha hotfix 15 — Inferencia y rutas con autoselección

- Basado en **hotfix 13**, sin incorporar los cambios de interfaz del hotfix 14. Primero resuelve la ruta exacta dentro de cada fuente; admite `NombreFuente/carpeta/archivo.ext`. Si falla, infiere por nombre del archivo y categoría del bloque, y recurre a otros medios de extensión compatible.
- Cuando se escribe una extensión, se filtran todas las candidatas por ella. Así `[IMAGEN] bart05.gif` acepta un GIF registrado como render y `[VIDEO] RAYOS X 2.avi` acepta un AVI registrado como efecto visual.
- Las coincidencias aproximadas y los nombres repetidos asignan de forma estable la mejor candidata. **Estado** enseña otras rutas y el tooltip permite leerlas completas. La validez del borrador depende de un campo lógico, no del texto que muestra la columna.
- Si no existe ninguna candidata compatible, el bloque permanece pendiente. Confirmar compilación y resultado con una biblioteca real en Windows; este entorno no tiene SDK .NET ni VEGAS.

### Prompt de validación hotfix 15

```text
[FONDO] Seiya/master creepy/callendo.png
[MOSTRAR] Bart | derecha | medio cuerpo
[TRANSICION] cruce | duracion=800 | vegas=flash | capas=fondo
[FONDO] calle | transicion=flash
[IMAGEN] bart05.gif | transicion=disolvente
[VIDEO] RAYOS X 2.avi | capa=sobre | duracion=20000 | transicion=barrido
Bart: El Director eligió recursos y conservó las alternativas a la vista.
```

Adapta los nombres a la Biblioteca del proyecto. Prueba un camino inexistente con un nombre existente, por ejemplo `ninguna/calle.png`: debe caer a la inferencia por `calle.png`. Prueba `bart0.gif` para ver si vincula automáticamente el GIF más cercano. Si hay dos `calle.png`, consulta **Estado** y escribe la ruta completa de la alternativa deseada para fijarla explícitamente.

## v0.0.8-alpha hotfix 13 — Referencia a `Path`

- Añade `using System.IO;` en `MainWindow.Director.cs`. El proyecto WPF no resolvía `Path.GetExtension`, y el compilador propagaba el error al tipo de `extension` y a la comparación de extensiones.
- La lógica de búsqueda y las demás piezas de hotfix 12 no cambian. El SDK .NET no está disponible en este entorno; confirmar con `dotnet build` en Windows.

## v0.0.8-alpha hotfix 12 — Búsqueda por nombre real y sugerencias

- El catálogo guarda `DisplayName` **sin extensión**. El Director antes buscaba `bart05.gif` literalmente en esa columna y devolvía vacío aunque el archivo estuviese catalogado; ahora separa el nombre y la extensión y compara contra la ruta real.
- La búsqueda global considera archivos visuales con formato compatible aunque otra regla de carpeta los haya clasificado como Render o Efecto visual. Al editar o seleccionar el archivo manualmente, el bloque conserva esa asignación.
- Si el nombre no coincide, el borrador enseña hasta tres rutas similares. Si coincide con varios archivos, pide la ruta relativa para evitar una selección errónea. Los recursos ausentes siguen excluidos.
- Probado con una base SQLite sintética que incluye `renders/Bart/bart05.gif` como Render y `VFX/RAYOS X 2.avi` como Efecto visual. Compilación y catálogo real pendientes de validar en Windows.

### Prompt de validación hotfix 12

```text
[FONDO] calle | transicion=heredar
[MOSTRAR] Bart | derecha | medio cuerpo
[TRANSICION] cruce | duracion=800 | vegas=flash | capas=fondo
[FONDO] calle | transicion=flash
[IMAGEN] bart05.gif | transicion=disolvente
[VIDEO] RAYOS X 2.avi | capa=sobre | duracion=20000 | transicion=barrido
Bart: Se encontraron el GIF y el video por su nombre de archivo.
```

Usa exactamente los nombres que aparezcan en **tu** Biblioteca. Si hay dos `bart05.gif`, escribe su ruta dentro de la fuente, por ejemplo `[IMAGEN] renders/Bart/bart05.gif`. Para probar sugerencias, cambia temporalmente `bart05.gif` por `bart0.gif`; comprueba el mensaje en **Estado** y vuelve a corregir el nombre antes de aplicar.

## v0.0.8-alpha hotfix 11 — Cruces independientes por medio visual

- Corrige el fallo CS0103 en los cinco usos de `SceneComposer` del Director.
- En cada Fondo, Mostrar, Imagen/GIF y Video, **Al entrar en un cruce** permite heredar, cortar a mitad, fundir normalmente, disolver, aplicar Flash o barrer. `transicion=` del Director acepta las mismas opciones. Los ajustes por bloque sustituyen `capas=` y `vegas=` solo para ese medio entrante.
- La temporización guardada, el plan de escena, la preview y el evento exportado para VEGAS leen el mismo ajuste; Flash sobre el fondo queda por debajo de los renders. Los videos que sustituyen a otro video activo comparten pista durante el solape.
- En este entorno no hay SDK .NET ni VEGAS para ejecutar `dotnet build` o abrir el proyecto nativo; validar ambos en Windows.

### Prompt de validación hotfix 11

```text
[FONDO] casa
[MOSTRAR] Bart | izquierda | medio cuerpo
Bart: Ahora cambiaremos varias capas por separado.
[TRANSICION] cruce | duracion=800 | vegas=flash | capas=fondo
[FONDO] calle | transicion=flash
[MOSTRAR] Bart | derecha | medio cuerpo | transicion=corte
[IMAGEN] humo.gif | transicion=disolvente
[VIDEO] lluvia.mp4 | capa=sobre | duracion=20000 | transicion=barrido
Bart: El fondo destella, el render corta y cada medio tiene su propio efecto.
[TRANSICION] cruce | duracion=800 | vegas=disolvente | capas=fondo,videos
[FONDO] casa | transicion=heredar
[MOSTRAR] Bart | izquierda | medio cuerpo | transicion=fundido
[IMAGEN] estrella.png | transicion=corte
[VIDEO] humo.mp4 | capa=sobre | duracion=2400 | transicion=flash
Bart: En el segundo cruce también cambié los efectos.
```

Sustituye los nombres por recursos **catalogados**. Comprueba en VEGAS que el fondo y el render no reciben el mismo plugin en el primer cruce, el GIF aparece con disolvente, la primera entrada de video lleva barrido y la segunda Flash. El render y la imagen marcados `corte` deben entrar a mitad sin solape con su recurso previo. Si quieres comprobar solape **entre dos videos**, ponlos en la misma capa y deja activo el primero al comenzar el segundo cruce; para probar GIF contra GIF, usa dos imágenes GIF consecutivas.

## v0.0.8-alpha hotfix 10 — Selección de capas para el cruce

- Los bloques **Cruce de capas** permiten escoger Fondo, Personajes, Imágenes/GIF y Videos. Todos siguen seleccionados por defecto; los bloques antiguos conservan el mismo resultado. El Director acepta `capas=fondo` o listas separadas por coma.
- Solo los tipos marcados se solapan y reciben el efecto VEGAS. Un recurso nuevo de tipo excluido se corta a mitad de la transición. Si no hay recurso nuevo de ese tipo, el existente permanece visible.
- La preview limita el destello blanco al fondo cuando solo está marcado Fondo; personajes, imágenes y videos elegidos reciben su propio destello respetando el canal alfa.
- Sin bloque Transición, `[MOSTRAR] Bart | derecha` sustituye al anterior por corte en el tiempo actual. Para cambiar fondo con Bart intacto, basta con incluir el nuevo `[FONDO]` sin repetir `[MOSTRAR]`.

### Prompt de validación hotfix 10

```text
[FONDO] casa
[MOSTRAR] Bart | izquierda | medio cuerpo
Bart: Voy a cambiar de posición sin fundido.
[MOSTRAR] Bart | derecha | medio cuerpo
Bart: Ya estoy a la derecha.
[TRANSICION] cruce | duracion=800 | vegas=flash | capas=fondo
[FONDO] calle
Bart: Yo sigo visible mientras cambia el fondo.
[TRANSICION] cruce | duracion=800 | vegas=flash | capas=fondo,personajes
[FONDO] casa
[MOSTRAR] Bart | centro | medio cuerpo
Bart: Esta vez sí entré en el Flash.
```

Sustituye `casa`, `calle` y `Bart` por elementos catalogados. La primera aparición a la derecha es un corte; el primer Flash debe iluminar solo el fondo y conservar a Bart; el segundo debe fundir tanto el fondo como el render. Comprueba en VEGAS que el primer cruce solo tiene icono Flash en el fondo y el segundo en ambas pistas.

## v0.0.8-alpha hotfix 9 — Fundido en renders y videos

- En **Cruce de capas** todos los medios visuales entrantes, incluidos renders de personaje, imágenes, GIF y videos, comienzan al principio de la transición y reciben FadeIn con el efecto VEGAS elegido. Los visuales que sustituyen a otro del mismo tipo comparten una pista para que VEGAS aplique el plugin sobre el solape real.
- Los videos nuevos que reemplazan un video aún activo en la misma capa heredan su pista y el anterior termina al acabar el solape. Otros videos simultáneos conservan sus pistas independientes.
- El Flash nativo se aplica ahora tanto al fondo entrante como al render entrante. En la preview MP4, personajes, props y videos superpuestos salientes también se atenúan mientras aparece el reemplazo; Flash mantiene un destello blanco aproximado sobre la escena entera.
- **Cambio a mitad** sigue disponible para sustituir un render sin mostrar dos siluetas durante la transición. Una disolución normal entre poses en posiciones distintas puede mostrar ambas durante el fundido.
- Debe ejecutarse `dotnet build` en Windows y comprobarse el script exportado en VEGAS 21.

### Prompt de validación hotfix 9

```text
[FONDO] casa
[MOSTRAR] Bart | izquierda | medio cuerpo
Bart: Voy a cambiar de escena.
[TRANSICION] cruce | duracion=800 | vegas=flash
[FONDO] calle
[MOSTRAR] Bart | derecha | medio cuerpo
Bart: El fondo y yo pasamos por Flash.
```

Sustituye los nombres por recursos catalogados. Exporta de nuevo a VEGAS y verifica un solape de 800 ms con el icono Flash en **dos pistas**: la del fondo y la de Bart. Para ver un cruce sin destello, cambia `vegas=flash` por `vegas=ninguno`; para eliminar la doble silueta, cambia `cruce` por `cambio`.

## v0.0.8-alpha hotfix 8 — Solape nativo y cambio de pose limpio

- Los fondos sucesivos ocupan la misma pista de VEGAS: al solaparse, el FadeIn del segundo evento crea una transición editable. Los renders sucesivos de un mismo personaje también comparten pista.
- En un cruce de fondos, el render anterior termina a mitad de la transición y el siguiente comienza allí. Así nunca quedan dos poses simultáneas del mismo personaje en lugares distintos. El audio siguiente conserva su inicio al terminar la transición.
- El plugin Flash suave (del inventario de VEGAS 21 entregado) se busca por ID y por nombre. El script muestra el motivo si no consigue aplicar el efecto. El MP4 incluye un destello blanco aproximado; Disolvente y Barrido conservan sus opciones nativas.
- La división de clips por cámara se suspende durante el solape y puede reanudarse al terminar, para no romper el cruce en varios eventos de la misma pista.
- Sin .NET/VEGAS en este entorno: requiere `dotnet build`, reexportar y abrir `Abrir_en_VEGAS_14_o_superior.cs` en un proyecto vacío para verificar el icono Flash en el solape.

### Prompt de validación hotfix 8

Sustituye los nombres por fondos y personajes catalogados:

```text
[FONDO] casa
[MOSTRAR] Bart | izquierda | medio cuerpo
Bart: Voy a cambiar de escena.
[TRANSICION] cruce | duracion=800 | vegas=flash
[FONDO] calle
[MOSTRAR] Bart | derecha | medio cuerpo
Bart: Ahora estoy aquí y solo hay un Bart.
```

En el `.veg`, verifica que los dos fondos se solapan en **una misma pista** durante 800 ms, que Flash figura en el solape y que los dos eventos de Bart están contiguos en su propia pista con el corte a mitad. La preview MP4 debe mostrar el destello y una sola pose de Bart en cada instante.

## v0.0.8-alpha hotfix 7 — CS0136

La compilación en Windows detectó `CS0136` en `MainWindow.Composition.cs` al
declarar `durationMs` para una transición y para toda la escena dentro del mismo
método. Se renombró la variable de la transición como `transitionMs`.
Conserva las funciones y la corrección del hotfix 6; compilar en Windows para validar.

## v0.0.8-alpha hotfix 6 — CS8361

La compilación en Windows detectó `CS8361` en el filtro del fundido de `SceneComposer.cs`.
La expresión condicional de duración ahora se evalúa antes de interpolar la cadena.
Conserva todas las funciones del hotfix 5. Pendiente ejecutar `dotnet build` en Windows.

## v0.0.8-alpha hotfix 5 — Cortes a mitad, solapes y transiciones VEGAS

- **Cambio a mitad (negro)** sitúa fondo y renders nuevos en el centro del fundido: no obliga al plano anterior a durar hasta el final.
- **Cruce de capas** solapa visuales antiguos y nuevos; los entrantes reciben un fundido de entrada durante la duración elegida. La preview MP4 usa opacidad y VEGAS asigna el efecto nativo opcional al FadeIn del evento entrante.
- Efectos disponibles en el inventario recibido de VEGAS 21: `disolvente` → Disolvente aditivo; `flash` → Flash suave; `barrido` → Barrido lineal, Izquierda-derecha, borde suave. Si un plugin o preset no está instalado, el script conserva el fundido y avisa.
- El fondo y los renders que siguen inmediatamente al bloque Transición empiezan juntos; el siguiente diálogo espera a que termine. `escena.json` registra posición, duración, solape y efecto de cada evento.
- La exportación a VEGAS y la preview con medios reales requieren comprobación en Windows con .NET y VEGAS. La comprobación sintética de FFmpeg cubre el fotograma negro a mitad y el cruce de opacidad.

### Prompt para validar hotfix 5

Sustituye `casa`, `calle` y `Bart` por nombres que tengas catalogados; usa dos renders distintos de Bart si tu biblioteca lo permite.

```text
[FONDO] casa
[MOSTRAR] Bart | izquierda | medio cuerpo
Bart: Aquí estoy frente a la casa.
[TRANSICION] cambio | duracion=800
[FONDO] calle
[MOSTRAR] Bart | derecha | medio cuerpo | rotacion=0
Bart: Ahora estamos en la calle.
[TRANSICION] cruce | duracion=800 | vegas=flash
[FONDO] casa
[MOSTRAR] Bart | centro | primer plano
Bart: De vuelta con una transición especial.
```

Al reproducir, comprueba negro cerca del centro del primer cambio y dos versiones solapadas durante el segundo. Exporta la escena a VEGAS 21 y verifica que los eventos nuevos empiezan antes de que acaben los anteriores y tienen FadeIn de 800 ms con **Flash suave**. Prueba también `vegas=barrido` o `vegas=disolvente` para comparar los presets. Para comprobar el corte limpio, cambia `cruce` por `cambio` y verifica que la capa anterior termina en el punto medio.

## v0.0.8-alpha hotfix 1 — Cámara compartida y fondo

- Primer plano ya no recorta agresivamente renders de medio cuerpo; aplica un acercamiento leve al plano completo.
- El fondo y todas las capas siguen el mismo zoom. VEGAS divide los eventos visuales según los cambios de cámara y conserva las pistas editables.
- Detección conservadora de bordes uniformes incorporados al archivo del fondo; casilla para desactivarla por bloque.
- Nuevas firmas de preview y exportación para regenerar los medios sin sobreescribir la versión anterior.

## v0.0.8-alpha — Presets y distribución de personajes

- Selección original/automático/entero/medio/primer plano; escenas ya creadas mantienen su encuadre original.
- El modo automático recorta bordes transparentes, estima encuadre por proporción visible y asigna posiciones estables cuando comparten plano varios personajes.
- FFmpeg y VEGAS comparten la geometría calculada. Exportaciones nuevas usan carpeta `_v8_720p` o `_v8_1080p` y conservan originales como segunda toma.
- El cambio de hablante aún no mueve ni corta la cámara; es trabajo de la siguiente fase.

## v0.0.7-alpha hotfix 2 — Encuadre visual y 720p/1080p

- Selector de resolución VEGAS en Preview: 1280×720 o 1920×1080, con proyecto 16:9 a 25 fps.
- Cada pista visual usa un lienzo completo con su geometría calculada desde las coordenadas del compositor. El fondo cubre el fotograma preservando relación de aspecto; renders, props, GIF y videos conservan posición, desplazamiento e inversión.
- PNG con alfa para estáticos y ProRes 4444 para animados; originales como tomas alternativas y voces/SFX/música sin conversión. Carpeta `_hf2_720p`/`_hf2_1080p` separada de entregas anteriores.
- FFmpeg sintético verificó fondo sin bandas, imagen transparente y video animado con croma a 1080p. Falta cotejar con VEGAS y recursos reales.

## v0.0.7-alpha hotfix 1 — Alfa y croma en VEGAS

- Videos con transparencia y clips con croma se preparan como ProRes 4444 con canal alfa y se importan en modo Straight; el original sigue disponible como toma alternativa.
- La exportación conserva la pista, el tiempo y la ruta original de todos los demás medios.
- Pendiente: validar con VEGAS la importación de los MOV reales, los bordes del croma y el encuadre fino.

## v0.0.7-alpha

- VEGAS Bridge inicial: scripts para las API Sony (12/13) y ScriptPortal (14+), manifiesto inspeccionable, pistas editables y rutas originales.
- El orden de los clips visuales replica el orden del compositor; SFX, voces y música ocupan pistas independientes para conservar solapamientos.
- Encaje fino, inversión y croma pendientes de validación en VEGAS: se conservan sus parámetros en `escena.json`.

## v0.0.6-alpha hotfix 3 — Transformaciones de todos los recursos visuales

- Fondo, render de personaje, imagen/prop (incluido GIF) y video comparten tamaño máximo, desplazamiento X/Y e inversión horizontal/vertical.
- El compositor FFmpeg aplica esas propiedades a cada tipo, con tamaños iniciales por tipo: fondo 1280 × 720, render 1280 × 610, imagen 500 × 400 y video 960 × 700. Se conservan las opciones de video guardadas por hotfix 2.
- El Director conserva izquierda/centro/derecha también al crear un bloque de imagen o video.
- Fondo fijo en video mantiene la composición a pantalla completa y su inversión; el tamaño/posición se deshabilita en ese modo.
- Se incrementa la huella de preview para rehacer MP4 existentes bajo la nueva composición.
- Texto en pantalla y Transición se identifican como bloques conservados en el guion que aún no se dibujan en el MP4.

---

## v0.0.6-alpha hotfix 2 — Tamaño, inversión y preview sin bloqueo

- Videos superpuestos: ancho y alto máximos (predeterminados 960 × 700 px), conservando la relación de aspecto, y desplazamiento fino X/Y.
- Controles separados para invertir horizontal y verticalmente en videos con alfa, pantalla verde y otros videos; Fondo fijo también permite invertir, aunque llena el cuadro.
- El MP4 se escribe bajo un nombre dependiente del contenido de la escena. Cambiar posición varias veces deja de intentar reemplazar un archivo abierto por el reproductor de Windows; se reutiliza el MP4 si el guion no cambió.
- Próximos controles visuales: encuadre/corte y aplicación de escala y volteo a renders, GIF e imágenes.

---

## v0.0.6-alpha hotfix 1 — Posición de videos superpuestos

- Los videos colocados en Orden del guion o Encima de todo caben dentro de 640 × 610 px, conservando su relación de aspecto. Así izquierda, centro y derecha tienen desplazamientos visibles incluso con material horizontal de pantalla completa.
- Los videos usados como Fondo fijo siguen llenando los 1280 × 720 px de la escena; en ese modo la posición queda desactivada en el editor.
- La huella del preview cambia para regenerar escenas antiguas con el nuevo tamaño de video.
- Prueba FFmpeg con MOV alfa y video croma a 1280 × 720: el objeto rojo se desplaza 480 px entre izquierda y derecha en ambos formatos.
- Siguiente control visual: ancho/alto, escala, desplazamiento fino y opciones de encuadre/recorte sin deformar el material.

---

## v0.0.6-alpha — Director y controles de escena

- Editor de bloques desplazable y barra estable para Nuevo/Guardar, también al cargar miniaturas y cambiar de tipo.
- Primera pestaña Director: instrucciones por líneas, sugerencias desde Biblioteca, borrador revisable y aplicación al guion con protección frente a cambios simultáneos.
- Documentación de hotfixes reunida aquí para seguir incrementando este historial.

---

## HOTFIX_0.0.5-alpha.12

## v0.0.5-alpha hotfix 12 — error CS8602

La selección de assets puede dispararse durante la inicialización de la ventana. Ahora el manejador comprueba al principio que los controles necesarios ya existen; esto evita la desreferencia posible que `dotnet build` reportaba en `MainWindow.AssetPicker.cs` línea 37. No cambia el compositor ni los proyectos existentes.

Este entorno no dispone de .NET ni WPF. Compila en Windows con `dotnet build` para validar la corrección en tu SDK.

---

## HOTFIX_0.0.5-alpha.11

## v0.0.5-alpha hotfix 11 — audio y orden de composición

Narración, SFX y música aceptan una duración opcional en milisegundos. Vacío conserva la duración original de narración/SFX y deja la música hasta el final de la escena. **Recortar / repetir** trunca el audio si la duración es menor y lo repite si es mayor. **Ajustar velocidad** cambia el tempo para encajar en la duración indicada sin modificar intencionadamente el tono. El SFX marcado «Esperar» desplaza lo siguiente según la duración ajustada; la voz de diálogo conserva su duración original. El WAV de narración sigue en caché; el ajuste ocurre en la composición.

Las capas de personaje, imagen/GIF y video normal ahora siguen el orden de los bloques del guion: un video posterior se dibuja delante de un personaje o GIF anterior. Video permite forzar **Fondo fijo** o **Encima de todo**. Los videos guardados en hotfix10 con el valor predeterminado `fondo` pasan a «Orden del guion». El formato de firma de preview cambió para regenerar los MP4 existentes.

Los videos previos sin una elección explícita de croma cuyo nombre contenga «Pantalla Verde», «Green Screen» o «Greenscreen» activan el croma por defecto; al volver a guardar el bloque, la casilla permite dejarlo activado o desactivarlo. Los nuevos videos con esos nombres proponen automáticamente la casilla. Color y tolerancia continúan editables; el valor inicial de tolerancia para nuevos bloques es 0.30. La captura recibida mostraba un verde aproximado `#22E000`, que el filtro elimina con tolerancia 0.15 en la comprobación con FFmpeg; también se comprobó el orden de varias capas contra ese color.

Se probaron con FFmpeg recorte, repetición, audio más largo y más corto ajustado con `atempo`, y un video transparente superpuesto sobre personaje e imagen. Falta compilar el proyecto WPF en Windows con `dotnet build` y probar con los archivos reales de la biblioteca.

---

## HOTFIX_0.0.5-alpha.10

## v0.0.5-alpha hotfix 10 — tiempo visual y video

Cada bloque Fondo, Mostrar personaje, Imagen / prop (incluidos GIF) y Video puede tener una duración visual en milisegundos. Vacía significa: fondo hasta el siguiente fondo; personaje hasta Ocultar o Mostrar nuevamente ese personaje; prop hasta el siguiente prop; video durante la duración original del archivo. Un valor explícito corta la imagen antes y, cuando es el último evento, amplía la escena lo necesario. La duración visual no retrasa la voz: para dejar silencio entre bloques usa Pausa ms. Un video o GIF vuelve a empezar si su duración visual supera la del archivo.

Video ya participa en el MP4. Se puede poner como fondo o sobre los personajes, con posición izquierda, centro o derecha para la segunda opción. Los MOV que decodifican alfa se superponen sin croma. Para un video con pantalla verde marca Pantalla verde y ajusta color hexadecimal RRGGBB y tolerancia entre 0.01 y 1.00. El audio del video no se incorpora a la mezcla en esta versión; para sonido usa los bloques SFX y Música.

La composición recorta las capas visuales en el intervalo asignado y comienza la animación al inicio del bloque, aunque el bloque aparezca tarde. El video resultante se actualiza al cambiar estas opciones. Se probaron los filtros de FFmpeg con MOV QTRLE transparente y MP4 verde, verificando los fotogramas antes, durante y después de cada capa.

Este entorno no tiene SDK de .NET ni WPF. Ejecuta `dotnet build` en Windows y comprueba la generación y reproducción con tus MOV/GIF/videos reales antes de cerrar la .5.

---

## HOTFIX_0.0.5-alpha.9

## v0.0.5-alpha hotfix 9 — carpetas generales y GIF en preview

Al seleccionar una carpeta de origen o una carpeta superior para un bloque con recursos (fondo, imagen/prop, SFX, música o video), **Incluir subcarpetas** queda activo por defecto. La lista vuelve a mostrar hasta 150 resultados catalogados de toda la carpeta seleccionada, con los tipos permitidos para el bloque. Buscar y **Elegir archivo…** permiten acceder a los restantes. Puedes desmarcar Incluir subcarpetas para limitar la lista a los archivos directos.

**Mostrar personaje** mantiene el comportamiento anterior: solo la carpeta exacta elegida salvo que marques expresamente Incluir subcarpetas. Al abrir un bloque ya guardado se respeta su opción previa; Elegir archivo… sigue seleccionando el archivo exacto y deja la opción desactivada.

El compositor usa `-stream_loop -1` para entradas GIF. FFmpeg no reconoce `-loop 1` en archivos GIF, por lo que estos fallaban al generar la escena. Las imágenes estáticas conservan `-loop 1`. Se comprobó la generación de un MP4 de dos segundos que mezcla PNG y GIF animado.

Este entorno no dispone del SDK de .NET: compila en Windows con `dotnet build` y comprueba un GIF de tu biblioteca para validar en tu instalación de FFmpeg.

---

## HOTFIX_0.0.5-alpha.8

## v0.0.5-alpha hotfix 8 — archivo directo y miniatura

En un bloque que usa assets, **Elegir archivo…** abre el Explorador en la carpeta activa. Puedes elegir un archivo que haya quedado fuera de los primeros 150: el editor lo busca por fuente y ruta exacta en SQLite, comprueba que esté catalogado y pertenezca a una categoría admitida, selecciona automáticamente su carpeta y deja el asset elegido para guardar el bloque. Elegir una ruta que no pertenece al catálogo muestra un aviso para escanearla en Biblioteca.

Los bloques Fondo, Mostrar personaje e Imagen / prop muestran una miniatura **del archivo seleccionado**, decodificada a tamaño pequeño. La miniatura no carga el resto de la biblioteca y admite que ciertos formatos, como algunos WebP o videos clasificados como meme, no tengan vista previa.

**Imagen / prop** entra ahora al compositor MP4: se dibuja delante de los personajes, según su posición izquierda/centro/derecha, hasta el siguiente bloque Imagen / prop o hasta el final de la escena. Si el meme catalogado es video, se repite como visual mientras dure el bloque; su audio no se mezcla. Esta primera capa admite un prop activo a la vez.

Continúan disponibles la búsqueda y las consultas acotadas a 150 resultados; el límite protege al desplegable sin impedir acceder a otros assets. Próxima entrega: galería visual por carpeta, controles de duración/capas y después el borrador de escena dirigido por prompt.

Este entorno no dispone del SDK de .NET. Compila en Windows con `dotnet build` antes de probar los cambios.

---

## HOTFIX_0.0.5-alpha.7

## v0.0.5-alpha hotfix 7 — categorías de Imagen / prop

El bloque **Imagen / prop** muestra únicamente los assets catalogados como **Props / objetos** y **Memes / relleno** en Biblioteca. El filtro se aplica también a las fuentes y subcarpetas disponibles, a la búsqueda por nombre y a la validación al editar un bloque. Los assets clasificados como renders de personajes, efectos visuales, overlays o sin clasificar ya no aparecen en ese selector.

Conserva la carga acotada, la navegación por carpetas y las demás mejoras del hotfix 6. Si cambias una regla de clasificación, vuelve a escanear la fuente y pulsa `↻` en el editor.

Este entorno no dispone del SDK de .NET; se requiere `dotnet build` en Windows para verificar el compilado.

---

## HOTFIX_0.0.5-alpha.6

## v0.0.5-alpha hotfix 6 — selector de assets escalable

El editor ya no intenta cargar todos los assets de «Imagen / prop», fondos, SFX, música o video en un solo ComboBox. Selecciona la fuente en **Carpeta**, entra en las subcarpetas del tipo seleccionado y usa **↑** para volver. «Elegir…» permite ir directamente a cualquier carpeta catalogada; «Incluir subcarpetas» amplía la búsqueda.

El desplegable carga un máximo de 150 elementos por consulta y muestra `150+` si hay más. **Buscar** consulta el catálogo por nombre después de dejar de escribir; puedes encontrar archivos fuera de los primeros 150 sin cargar toda la biblioteca. El control de carpetas aparece solo en bloques que utilizan assets. La selección de carpeta y el check se conservan al guardar y editar el bloque. Los bloques anteriores reconstruyen la carpeta desde su asset.

La lista de subcarpetas visibles de cada nivel se limita a 300; para rutas fuera de esa lista se usa «Elegir…». Las consultas de archivos usan una conexión SQLite de lectura independiente. Un bloque SFX conserva su carpeta al cambiar «Esperar a que termine el SFX». Las referencias a assets de escenas grandes se consultan en lotes de 400.

La validación local incluyó una base de prueba de 100 000 registros, comprobación de XAML y del ZIP. Como este entorno no tiene SDK de .NET, compila en Windows con `dotnet build` antes de ejecutar.

---

## HOTFIX_0.0.5-alpha.5

## v0.0.5-alpha hotfix 5

Corrige CS8600 y CS8604 en `MainWindow.SpritePicker.cs`: la variable local de carpetas conserva un array no nulo, y el resultado de `TryGetValue` se asigna únicamente cuando la consulta de caché tiene éxito. Incluye todas las funciones de hotfix 4.

En Windows: `dotnet build`. El SDK de .NET no está instalado en el entorno donde se preparó este paquete.

---

## HOTFIX_0.0.5-alpha.4

## v0.0.5-alpha hotfix 4 — carpetas de personajes

En «Mostrar personaje», el selector sugiere la carpeta que lleva el nombre exacto del personaje. Por ejemplo, para Bart dentro de `BART FLUTTER SHY Y FLIA/BART` solo carga los renders directamente bajo `BART`. El check «Incluir subcarpetas» amplía el filtro. «Elegir…» permite seleccionar cualquier carpeta dentro de una fuente catalogada, incluso cuando «Personaje» está vacío; el render elegido también aparece en el MP4.

El campo «Buscar» filtra por nombre entre los renders cargados. La carpeta, la opción de subcarpetas y la posición se guardan con cada bloque para que su elección reaparezca al editarlo. Los bloques de versiones anteriores intentan recuperar la carpeta a partir del asset previamente elegido.

Para imágenes nuevas dentro de una fuente, escanea la biblioteca y después pulsa `↻` en el editor. Próximo paso del roadmap: miniaturas para escoger expresiones de un vistazo y validación del compositor con escenas reales en Windows.

El SDK de .NET no está disponible en este entorno. Compilar en Windows con `dotnet build` antes de probar el ejecutable.

---

## HOTFIX_0.0.5-alpha.3

## v0.0.5-alpha hotfix 3

Corrige CS0136 en `MainWindow.Script.cs`: la revisión de caché de la rama «Mostrar personaje» ahora utiliza `characterRevision`, sin colisionar con la variable `revision` de la rama general. Incluye todo lo agregado en hotfix 1 y 2.

El SDK de .NET no está instalado en este entorno: el proyecto debe compilarse en Windows con `dotnet build`.

---

## HOTFIX_0.0.5-alpha.2

## v0.0.5-alpha hotfix 2 — Selección de renders por personaje

En «Mostrar personaje», elegir primero un personaje carga únicamente renders con ese `subject_name` en el catálogo o cuyo directorio de origen contiene su nombre. La consulta filtra en SQLite antes de crear los elementos de la interfaz; entrar en el tipo de bloque sin elegir personaje ya no carga todos los sprites de la biblioteca. Los resultados de cada personaje se reutilizan hasta que cambie el catálogo. La selección de bloques antiguos conserva su render aunque quede fuera del filtro, identificado como tal.

Para carpetas sin el nombre del personaje, define el sujeto en las reglas de la fuente y vuelve a escanear; el filtro de carpeta consulta el nombre del directorio, no el nombre del archivo. El campo «Imagen / prop» conserva su selector anterior y aún puede listar muchos sprites; una galería con búsqueda forma parte de las siguientes mejoras.

Se actualizó `docs/ROADMAP.md` para reflejar las fases ya entregadas y la siguiente integración prevista con VEGAS. En esta sesión se comprobaron la estructura XAML y una consulta SQLite equivalente con 48.828 registros; falta compilar/probar la interfaz WPF en Windows.

---

## HOTFIX_0.0.5-alpha.1

## v0.0.5-alpha hotfix 1

- Cambiar «Esperar a que termine el SFX» en un bloque ya guardado actualiza y guarda en ese momento sus tiempos y los de los bloques siguientes. Guardar, eliminar o reordenar bloques también recalcula el timeline sin generar el MP4.
- ▶ / ❚❚ funciona como botón único: pausa/reanuda el video, recupera el MP4 anterior si el guion y archivos no cambiaron, o sintetiza las voces pendientes y recompone el video antes de reproducir. La huella del video se guarda junto al MP4 para reutilizarlo también después de cerrar la aplicación.
- La advertencia MSB3026 de LoquendoAI.Cli indica que Windows impidió copiar el apphost durante un intento; si el reintento terminó y el build finalizó correctamente, no bloquea la app.
- Si el usuario modifica el guion mientras FFmpeg genera el MP4, esa operación ya no vuelve a guardar una versión vieja del timeline.

La compilación del hotfix requiere Windows y .NET 10; el SDK no está disponible en este entorno. Verifica en Windows con `dotnet build .\LoquendoAI.sln`.

---

## HOTFIX_0.0.4-alpha.1

## v0.0.4-alpha hotfix 1 — Assets del guion

### Causa confirmada en el código

`_loadedScriptAssetKinds` se marcaba como consultado incluso cuando la consulta devolvía cero archivos y solo se limpiaba al abrir otro proyecto. Un escaneo posterior podía catalogar SFX correctamente en SQLite sin hacerlos visibles en el ComboBox del guion.

### Correcciones

- Cada actualización de la biblioteca invalida el caché del editor de Guion y vuelve a consultar el tipo de bloque actual.
- Se conservan la selección y las referencias por GUID si el asset continúa disponible.
- Botón ↻ para forzar una recarga de los assets sin cerrar el proyecto.
- Contador de assets disponibles junto al selector y ayuda cuando el resultado es cero.
- Consultas de assets serializadas para evitar carreras entre `SelectionChanged`, `Nuevo bloque` y recargas, con guardia por versión de caché.
- No se alteran la clasificación, las reglas, el schema v4 ni los archivos originales.

### Prueba de regresión

1. En Guion selecciona SFX **antes** de escanear una carpeta que contenga WAV/MP3. El selector puede mostrar `Asset (0)`.
2. Escanea la fuente y comprueba que `Asset (N)` se actualiza y que el ComboBox muestra sus archivos sin reiniciar.
3. Cambia una regla de carpeta de SFX a Música y reescanea: los assets deben desaparecer de SFX y aparecer bajo Música.
4. Pulsa ↻ con SFX seleccionado para recargar manualmente.
5. Si sigue en `Asset (0)`, en Biblioteca busca `SFX` y revisa la columna de estado: los archivos marcados como `Faltante` no se ofrecen al guion.

---

## HOTFIX_0.0.3-beta.4

## v0.0.3-beta.4 — BALCON / Infovox compatibility pass

The first SAPI4 provider build could terminate with exit code 1 while SpeechPad itself still worked.
This pass avoids forcing neutral SAPI4 prosody, uses Unicode input, short voice identifiers and the
BALCON directory as WorkingDirectory. It also preserves BALCON stdout/stderr and adds a staged probe.

Important: extract the complete official BALCON ZIP into `tools\balcon\`; do not copy only balcon.exe.
The official distribution may include companion DLLs such as `libsamplerate.dll`, `chsdet.dll` and
`SoundTouch.dll`.

Diagnostic:

    scripts\tts-balcon-probe.cmd

or:

    artifacts\ttsbridge-win-x86\LoquendoAI.TtsBridge32.exe balcon-probe --voice "Antonio (Spanish) SAPI4 22kHz"

---

## HOTFIX_0.0.3-beta.3

## v0.0.3-beta.3 — Infovox / SAPI4 fallback

- Penélope/UTF-8 fix from beta.2 retained.
- Adds `balcon-sapi4` provider for legacy Infovox/SpeechPad voices.
- Uses BALCON's documented SAPI4 ranges: pitch 0..100, speed 0..100.
- Does not fake volume control for SAPI4; Voice Lab disables that slider.
- BALCON can be placed in `tools\balcon\balcon.exe` or configured with `LOQUENDO_AI_BALCON`.
- SAPI5 remains the provider for IVONA/modern voices; TTS7 Native remains preferred for Loquendo voices.

---

## HOTFIX_0.0.3-beta.2

## Loquendo AI v0.0.3-beta.2 — SAPI compatibility hotfix

Este hotfix corrige dos fallos observados en Voice Lab:

1. **IVONA 2 Penélope** podía aparecer en la lista pero fallar al sintetizar porque el nombre con `é` se corrompía entre el bridge x86 y la app al usar streams redirigidos con la code page del sistema. La comunicación de consola se fija a UTF-8 explícito en ambos extremos.
2. Las voces legacy de **Infovox SpeechPad / Acapela** podían terminar el bridge con `0xC0000409`. Voice Lab siempre enviaba pitch=0 y el provider SAPI lo convertía innecesariamente en XML `<pitch>`. El camino neutral ahora usa texto plano; XML sólo se usa si pitch es distinto de cero. También se mantiene el token COM seleccionado vivo hasta terminar `Speak`.

### Prueba recomendada

1. `scripts\run-voice-lab.cmd` para republicar el bridge y abrir la app.
2. Probar `IVONA 2 Penélope - Spanish (US) female voice [22kHz]` con pitch 0.
3. Probar Antonio/Javier/Maria/Rosa con pitch 0.
4. Si esas cuatro ya hablan, probar una de ellas con pitch +2. Si sólo falla con pitch no neutro, el wrapper Acapela no tolera el XML de prosodia y se tratará como capability del provider, no como fallo de síntesis base.

---

## HOTFIX_0.0.3-alpha.6

## Hotfix 0.0.3-alpha.6

TTS7 Native ya genera WAV correctamente. Esta entrega se concentra en QA antes del Voice Lab.

Incluye `qa-suite`, `stress`, `controls-test`, inspección RIFF/WAVE, `ttsSetSpeed` y launchers `.cmd`.

Orden recomendado: QA -> controls -> stress 100 -> stress 500 -> stress 1000.

---

## HOTFIX_0.0.3-alpha.5

## Hotfix 0.0.3-alpha.5

### Loquendo TTS7 native: ABI corregida

El `abi-probe` confirmó en una instalación real de Loquendo TTS7 Win32 que las
funciones nativas deben invocarse con `stdcall`. El provider principal todavía
usaba delegados `cdecl`, lo que hacía que `ttsNewReader` aparentara funcionar y
que el proceso terminara con `0xC0000409` al entrar a `ttsLoadPersona`.

Este hotfix cambia **todos** los delegados de `LoquendoTts7Provider` a
`CallingConvention.StdCall`, incluyendo consulta de voces, carga de persona,
configuración de audio, lectura y obtención de errores.

Se conserva el worker nativo aislado como frontera de seguridad ante fallos de
las DLL Win32 antiguas.

Prueba principal:

```powershell
.\artifacts\ttsbridge-win-x86\LoquendoAI.TtsBridge32.exe synth `
  --provider loquendo7-native `
  --voice "Jorge" `
  --text "Hola amigos de YouTube, esto es una prueba de Loquendo AI." `
  --out ".\jorge_native.wav"
```

Si falla después de `ttsLoadPersona`, las trazas indicarán la siguiente firma a
validar (`ttsSetAudio` o `ttsRead`) sin volver a confundirlo con un fallo de voz.

---

## HOTFIX_0.0.3-alpha.4

## v0.0.3-alpha hotfix 4

- Adds an isolated `abi-probe` command for the legacy Loquendo TTS7 native API.
- Tests `cdecl` and `stdcall` in separate child processes, so a wrong ABI cannot kill the parent diagnostic command.
- Focuses the probe on the exact failing boundary: `ttsNewReader` -> `ttsLoadPersona`.
- Documents the official Java RMI Remote API discovered in the supplied Loquendo 7.14 JARs.
- No database/schema changes.

---

## HOTFIX_0.0.3-alpha.3

## Hotfix 0.0.3-alpha.3

### LTTS7 native crash diagnostics

The native Loquendo provider is now executed in an extra isolated x86 worker process.
A hard native crash can no longer make the public CLI disappear silently: the parent
reports the worker exit code and the last LTTS7 stage reached.

Changes:
- Native synthesis uses LTTS7's implicit session (`ttsNewReader(..., NULL)`), matching
  the known-working historical `win32-loquendo` binding.
- Explicit `ttsDeleteSession`/`FreeLibrary` teardown is avoided in the short-lived worker.
- Flush-safe stage traces surround `ttsNewReader`, `ttsLoadPersona`, `ttsSetAudio` and `ttsRead`.
- Native worker exit codes are reported in decimal and hexadecimal (e.g. `0xC0000005`).
- A verified WAV is preserved if a legacy DLL crashes only during process teardown.
- Added `scripts\tts-native-test.cmd` so this diagnostic does not depend on PowerShell execution policy.

---

## HOTFIX_0.0.3-alpha.2

## Hotfix 0.0.3-alpha.2 — Loquendo native synthesis

Corrige el caso en el que `loquendo7-native` detectaba y enumeraba voces, pero el proceso terminaba antes de producir/confirmar el WAV.

Cambios:

- Inicialización explícita de sesión TTS7 mediante `ttsNewSession` cuando la exportación está disponible.
- Detección de `default.session` junto a `LoqTTS7.dll`/raíz LTTS7.
- `ttsRead` pasa a modo bloqueante (`async=false`) con texto en memoria (`fromFile=false`).
- Limpieza de recursos con `ttsDeleteSession(NULL)` al terminar.
- Se evita descargar `LoqTTS7.dll` manualmente mientras TTS7 conserva estado nativo global.
- Validación de que el WAV realmente exista y tenga contenido.
- Mensajes de error nativos mediante `ttsGetErrorMessage` cuando la DLL lo expone.
- TTS Doctor muestra el `default.session` utilizado.

El bridge sigue siendo win-x86 autocontenido.

---

## HOTFIX_0.0.3-alpha.1

## Hotfix 0.0.3-alpha.1 — self-contained x86 TTS bridge

### Problem
`LoquendoAI.TtsBridge32` is intentionally a 32-bit process. A normal `dotnet run` produces a framework-dependent x86 apphost, which requires an x86 .NET 10 runtime installed in `C:\Program Files (x86)\dotnet`. A machine with only the x64 SDK/runtime therefore builds successfully but cannot launch the x86 bridge.

### Fix
- Removed obsolete `Prefer32Bit`, eliminating `NETSDK1189`.
- Added `scripts/publish-ttsbridge-x86.ps1` to publish the bridge as **self-contained win-x86**.
- Added `scripts/tts-scan.ps1` and `scripts/tts-voices.ps1`; they publish automatically if the self-contained bridge is not present.
- No extra x86 .NET runtime installation is required for the published bridge.

### Recommended test
From the repository root:

```powershell
.\scripts\tts-scan.ps1
```

Then:

```powershell
.\scripts\tts-voices.ps1 -Provider sapi5-x86
.\scripts\tts-voices.ps1 -Provider loquendo7-native
```

The regular solution can still be built with `dotnet build .\LoquendoAI.sln`.
Do not use `dotnet run` for the x86 bridge unless an x86 .NET runtime is installed system-wide.

---

## HOTFIX_0.0.2.3

## Hotfix 0.0.2.3 — Clasificación Mixta

Corrige el comportamiento de `Mixto`, que antes compartía prácticamente la misma lógica conservadora de `Automático` y dejaba imágenes/audio sin clasificar.

### Nuevo comportamiento

1. Una regla explícita por carpeta sigue teniendo prioridad.
2. En `Mixto`, se aprovechan pistas de toda la ruta (`OST`, `sonidos`, `fondos`, `VFX`, `memes`, etc.).
3. Si no hay pista suficiente:
   - `.gif` -> `Prop`
   - audio -> `Audio` genérico
   - video -> `Video`
   - fuentes -> `Fuente`
   - otras imágenes soportadas -> `Render`
4. `Automático` permanece conservador y puede seguir dejando material ambiguo como `Sin definir`.

El nuevo `AssetKind.Audio` se añadió al final del enum para no cambiar los valores persistidos de categorías previas. No requiere migración de schema.

---

## HOTFIX_0.0.2.2

## Loquendo AI v0.0.2.2 — hotfix de bibliotecas grandes

Probado conceptualmente contra el caso real que destapó el problema: una fuente con ~67.000 assets.

### Problema

Al guardar cambios en las reglas de una fuente se lanza un reescaneo. El escáner era `async`, pero el recorrido del filesystem, parte del procesamiento por archivo y las actualizaciones de progreso todavía podían monopolizar el dispatcher de WPF. Con bibliotecas pequeñas pasaba desapercibido; con decenas de miles de archivos Windows llegaba a mostrar `No responde`.

Además, el progreso se notificaba una vez por archivo. Con 67.000 assets eso podía inundar la cola del dispatcher aunque el trabajo principal estuviera avanzando.

### Cambios

- El escaneo completo corre ahora en un worker de `Task.Run`.
- El worker abre su **propia conexión SQLite** para no trasladar la conexión de UI entre hilos ni bloquear la interfaz.
- Las notificaciones de progreso se limitan a 1 de cada 250 archivos, más una notificación final.
- Se añadió **Cancelar escaneo**.
- Mientras hay un escaneo, se bloquean las acciones que podrían mutar fuentes/proyecto, pero la ventana permanece responsiva.
- La reconstrucción de la tabla de biblioteca (lectura, agrupación y ordenado de decenas de miles de registros) también se hace fuera del hilo de UI.
- El conteo por fuente dejó de hacer un `Where(...).ToArray()` completo por cada fuente y ahora agrupa los assets una sola vez.

### Compatibilidad

No hay cambios de schema. Un proyecto creado con v0.0.2 / v0.0.2.1 se abre directamente.

### Flujo esperado

1. Abrir `Editar reglas…`.
2. Cambiar una carpeta.
3. `Guardar fuente`.
4. La ventana vuelve a Loquendo AI y muestra `Escaneando ... en segundo plano…`.
5. La UI debe seguir respondiendo.
6. `Cancelar escaneo` interrumpe el trabajo sin borrar los assets que ya estaban catalogados.

---

## HOTFIX_0.0.2.1

## Loquendo AI v0.0.2 hotfix 1

Fix de compilación de `LoquendoAI.App`.

### Corrección

Se agregó `using System.IO;` explícitamente a:

- `src/LoquendoAI.App/MainWindow.xaml.cs`
- `src/LoquendoAI.App/AssetSourceSetupWindow.xaml.cs`

Esos archivos usan `Path`, `Directory` y `DirectoryInfo`. El proyecto WPF no estaba resolviendo esos tipos mediante los implicit usings, lo que causaba los 21 errores CS0103/CS0246 reportados durante `dotnet build`.

No hay cambios en la base de datos, schema, proyecto ni comportamiento de la Asset Library.

---

## HOTFIX_0.0.1.1

## Loquendo AI v0.0.1.1 Hotfix

### Fixed

- Fixed `CS0266` in `SqliteProjectRepository.UpsertAssetTagsAsync`.
- `SqliteConnection.BeginTransactionAsync` is exposed through the base ADO.NET API as `DbTransaction`; Microsoft.Data.Sqlite commands require `SqliteTransaction`. The returned transaction is now explicitly cast to `SqliteTransaction`.

Changed line:

```csharp
await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
```

No database schema or project format changes.

---

## v0.0.8-alpha hotfix 2 — parámetros del Director

El Director por líneas acepta ancho, alto, desplazamientos X/Y, duración visual, volteos, control de bordes del fondo y opciones de capa/croma en video. Para narración, música y SFX admite duración y modo de audio. El borrador enseña los ajustes antes de aplicar y rechaza valores fuera de rango o parámetros desconocidos. Los cambios de plano conservan el comportamiento existente del compositor, que acerca la composición con el fondo vigente. Rotación y transiciones aún no se ejecutan y se rechazan expresamente.

Ejemplo:

```text
[FONDO] casa | bordes=si | ancho=1280 | alto=720
[MOSTRAR] Bart | izquierda | medio cuerpo | ancho=850 | alto=610 | x=30 | y=10
Bart: Hola, Shadow.
[MOSTRAR] Shadow | derecha | medio cuerpo | voltear h=si
Shadow: ¿Qué pasó?
[OCULTAR] Shadow
[MOSTRAR] Bart | centro | primer plano | ancho=980
Bart: Ahora el fondo se acerca conmigo.
[MUSICA] ambiente | duracion audio=12000 | modo audio=loop
```

## v0.0.8-alpha hotfix 3 — invertir y rotar visuales

Se añadió rotación en grados (-180 a 180, positivos en sentido horario) a Fondo, Mostrar personaje, Imagen/prop y Video. El control se guarda con el bloque, aparece al reabrirlo y se aplica a la preview y a los medios normalizados de VEGAS. El Director admite `rotacion=12` y los alias `invertir horizontal=si` e `invertir vertical=si`, además de `voltear h/v=si`. Rotación e inversión se aplican sobre el recurso antes de la cámara de escena. Los fondos y videos de fondo amplían su imagen para cubrir el cuadro al girar. Los clips generados para VEGAS incorporan una huella de los parámetros para evitar reutilizar una versión anterior del mismo bloque. Los archivos originales siguen como tomas alternativas.

### Prompt para validar

Sustituye `casa`, `Bart` y `Shadow` por los nombres que tengas catalogados. En el borrador comprueba la columna Ajustes antes de aplicarlo.

```text
[FONDO] casa | bordes=si | rotacion=4
[MOSTRAR] Bart | izquierda | medio cuerpo | ancho=850 | invertir horizontal=si | rotacion=-12
Bart: Parece que me incliné hacia el otro lado.
[MOSTRAR] Shadow | derecha | medio cuerpo | invertir vertical=si | rotacion=12
Shadow: Y yo estoy de cabeza.
[OCULTAR] Shadow
[MOSTRAR] Bart | centro | primer plano | ancho=980 | invertir horizontal=no | rotacion=0
Bart: Volvemos al plano normal y el fondo se acerca conmigo.
```

Las opciones de inversión vertical y rotación de fondo se incluyen en la prueba para comprobar que afectan tanto al MP4 como a la exportación a VEGAS; quítalas al montar una escena convencional. Las transiciones y la rotación animada con fotogramas clave siguen pendientes.

## v0.0.8-alpha hotfix 4 — primeros fundidos de escena

El bloque Transición ahora aplica un fundido de entrada desde negro o salida hacia negro de 80 a 10.000 ms. La duración forma parte del reloj del guion y pospone los bloques siguientes. El MP4 compone una capa negra animada sobre la cámara; el puente de VEGAS crea una pista superior editable con eventos y FadeIn/FadeOut de igual duración. Afecta a la imagen; la mezcla de audio no se atenúa. Bloques Transition guardados anteriormente sin `transitionStyle` siguen sin efecto hasta que se editen, para evitar cambios inesperados en proyectos existentes. Los efectos nativos de VEGAS todavía no se asignan automáticamente. Se incluyen scripts opcionales para inventariar sus plugins y presets instalados en VEGAS 12/13 y 14+.

### Prompt para validar

```text
[FONDO] casa
[MOSTRAR] Bart | izquierda | medio cuerpo
Bart: Mira, voy a cambiar de escenario.
[TRANSICION] fundido salida | duracion=500
[FONDO] calle
[TRANSICION] fundido entrada | duracion=500
[MOSTRAR] Bart | centro | medio cuerpo
Bart: Ya estamos afuera.
```

Confirma que el fotograma justo antes del nuevo fondo llega a negro, el siguiente aparece desde negro y VEGAS contiene la pista `Fundidos de escena` encima de los visuales. Cambia `casa`, `calle` y `Bart` por nombres catalogados. Para consultar los efectos nativos disponibles, ejecuta en VEGAS el script de `scripts/vegas` correspondiente a tu versión y comparte el archivo `transiciones_vegas.txt`.

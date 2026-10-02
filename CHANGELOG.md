## v1.4.8 — Biblioteca entre proyectos: importar y biblioteca principal
- **«Importar de otro proyecto…»** (pestaña Biblioteca): trae la biblioteca de otro proyecto (fuentes, reglas de carpeta, clasificación y etiquetas, también las de la IA), y si quieres sus perfiles de voz y sus personajes con su voz.
  - No duplica nada: la misma carpeta (aunque esté escrita distinto), el mismo archivo, o un perfil o personaje con el mismo nombre se reconocen. Lo que ya tenías conserva su identidad, así que tus escenas siguen igual; para los mismos archivos manda la clasificación importada.
  - El otro proyecto no se toca y puede estar abierto en otra ventana (se lee de una copia).
- **Biblioteca principal**: «Usar este proyecto como biblioteca principal» y, en los demás, la casilla «Seguirla». Un proyecto que la sigue recibe sus fuentes y recursos nuevos (y su clasificación) cada vez que se abre, solo si la principal cambió; «Actualizar ahora» lo hace al momento. Al crear un proyecto se ofrece seguirla. Solo la biblioteca: los perfiles y personajes se pasan con «Importar». Nada se borra en los proyectos que la siguen.

## v1.4.7 — Personajes de buen tamaño: carriles bien contados y «Revisar encuadre» que sí los ve
- **Corregido: personajes diminutos en escenas con cruces.** El encuadre automático reparte el ancho entre los personajes que están a la vez, pero contaba clips: durante un cruce el render que sale y el que entra del mismo personaje contaban como dos, y una escena de 2 personajes quedaba con carriles de 288 px en vez de 608 de principio a fin. Ahora cuenta personajes.
- **Con 4 o 5 en pantalla (por ejemplo 3 al frente y 2 detrás) ya no quedan diminutos:** el ancho de cada carril nunca baja de un tercio del cuadro (394 px). A los renders altos no les cambia nada; los anchos (ponies, sentados) dejan de encogerse y pueden encimarse un poco, como en cualquier composición con fila de atrás.
- **«Revisar encuadre» ve a los pequeños**, con 1 a 4 personajes en pantalla:
  - Contra el cuadro: el cuerpo de un personaje por debajo del 60 % del alto (45 % si es un render ancho) se avisa, aunque esté solo o todos hayan quedado igual de pequeños.
  - Contra quien está a su lado en ese momento: un 20 % más bajo (35 % para un render ancho).
  - Ya no descarta en silencio a los que limita el ancho de su carril: los avisa y dice por qué.
  - Reconoce la fila de atrás (tapado en parte por otro que se dibuja encima, con los pies más arriba): no la compara con los del frente y solo avisa si baja del 40 % del alto del cuadro, diciendo detrás de quién está.
- **«Escala» del render** (`escala=130` en el Director, «Escala %» en el Editor, que ahora se guarda con el personaje): agranda o achica lo que da el encuadre automático, y puede salir un poco de su carril. Es la corrección de tamaño de «Revisar encuadre»; antes agrandaba la caja del bloque, que el encuadre automático ignoraba.
- **Renders «sucios»**: un velo casi transparente, una sombra tenue o motas sueltas ya no hacen creer que el personaje ocupa todo el lienzo; el recorte sigue al personaje.
- **Imágenes sin transparencia** (Paint, fondo blanco): se mide la figura contra el color del fondo y «Revisar encuadre» avisa que se verá el recuadro.
- Props pequeños: además de los de 64 px o menos, los de menos de 100 px junto a personajes mucho más grandes.
- **«Revisar encuadre» automático o estricto** (casilla en la ventana; se recuerda): el automático muestra solo lo que tiene avisos; el estricto lista todos los renders y props de la escena con «Cambiar tamaño» y su porcentaje (también para achicar), y los que tienen avisos siguen trayendo su sugerencia primero.
- Tamaño y posición se corrigen por separado: agrandar un render al que ya le limitaste la animación conserva el límite, y al revés.
- Si un bloque tiene varios problemas, «Revisar encuadre» los muestra juntos en una sola fila.

## v1.4.6 — Transcripción con GPU (NVIDIA), voces grabadas a cualquier escena y versión en la cabecera
- **«Incorporar voces a la escena» funciona en cualquier escena**: la tabla de Voces grabadas ya no queda atada a la escena donde añadiste o transcribiste los WAV. Puedes transcribir con una escena seleccionada, crear otra (o ir a otro capítulo) e incorporarlas ahí. Las tomas cargadas con «Cargar voces de la escena» se siguen actualizando en su escena de origen. Solo cambiar de proyecto vacía la tabla.
- **La versión bajo el nombre del programa** sale de la compilación, así siempre coincide con la del release (antes se quedó en v1.4.2).
- **«Instalar soporte GPU»** en Voces grabadas: el portable trae el motor de Whisper con CUDA, pero no las librerías de NVIDIA que necesita (cuBLAS y cuDNN), por eso «cuda» fallaba con «cublas64_12.dll is not found». No van incluidas por su tamaño (≈1.3 GB de descarga, ≈1.8 GB en disco): el botón las descarga una sola vez, en las versiones exactas que pide el motor, en la carpeta de datos (`datos\soporte-gpu` en la portable, `%LOCALAPPDATA%\LoquendoAI\soporte-gpu` instalada): las versiones nuevas las conservan, como los modelos de Whisper. El botón desaparece cuando ya están.
- **«cuda» sin soporte GPU** ya no falla toma por toma: antes de empezar ofrece instalarlo y, si aceptas, transcribe al terminar.
- **«auto» sin soporte GPU** transcribe en CPU directamente (sin intentar la tarjeta) y, al terminar, recuerda que con el soporte GPU es mucho más rápido.
- Con «cuda», un error de la tarjeta a mitad del lote lo detiene con ese error en vez de repetirlo en cada toma.
- `scripts\stt-setup.ps1 -Gpu` instala lo mismo desde una consola. CTranslate2 queda fijo en 4.8.2 para que su cuDNN coincida con el que se descarga.

## v1.4.5 — Arreglo del STT (Voces grabadas)
- **La transcripción volvió a funcionar.** En la 1.4.4 todas las voces grabadas fallaban con «TypeError: open() got an unexpected keyword argument 'metadata_errors'»: el instalador del STT tomó PyAV 19, que quitó un argumento que faster-whisper todavía usa para abrir el audio. Ahora se instala PyAV 18 (`av<19`) y, por si una versión futura vuelve a cambiarlo, el worker reintenta abrir el audio sin ese argumento.
- Para arreglar una instalación existente sin descargar de nuevo: `worker\python\python.exe -m pip install "av<19"` (portable) o volver a ejecutar `scripts\stt-setup.ps1`.

## v1.4.4 — Director más seguro al aplicar, borradores separados, ocultar varios a la vez y revisión de encuadre
- **«Aplicar al guion» con reglas claras** (probadas una por una):
  - **Escena vacía**: el borrador la llena directamente, en cualquier modo.
  - **Escena sin cambios**: como siempre.
  - **Continuación** (modo historia, o prompt sin «Sustituir») sobre una escena que cambió: avisa «Se agregarán X bloques al final» y pregunta.
  - **«Sustituir»** marcado: siempre pregunta antes («¿Seguro? Se perderá la escena actual…»).
  - **Voces grabadas**: el borrador manda. Si la escena cambió desde que se generó, lo que quitaste vuelve (el borrador se hizo con esas líneas y los WAV siguen en disco) y lo que agregaste a mano va al final; antes de aplicar lo resume y pregunta.
  - **Otra escena**: si está vacía se llena; si no, avisa cuánto agrega o pregunta antes de sustituir.
  - Si al aplicar se dejarían de usar voces grabadas, sigue avisando.
  - Los avisos y errores se ven siempre en la pestaña del borrador (antes algunos solo salían en Director (prompt) y parecía que «no hacía nada»).
- **Borradores independientes**: Director IA y Director (prompt) tienen cada uno su borrador, su tabla, sus avisos y su «Aplicar». Para editar línea por línea un borrador de la IA: «Copiar borrador» y pegarlo en Director (prompt).
- **«Borrar borrador»** en las dos pestañas (el guion de la escena no cambia). Regenerar o preparar otro también lo reemplaza.
- **Borrar filas del borrador**: en Director (prompt) con Supr, y hay que volver a validar; en Director IA no. En un borrador de voces, los bloques originales no se quitan ahí: se editan en el editor de la escena.
- **Ocultar varios a la vez**:
  - Director: `[OCULTAR] Bart, Lisa` o `[OCULTAR] todos` (los que siguen en pantalla, NPC incluidos). Salen en el mismo instante; con `[PAUSA]` entre dos `[OCULTAR]` salen uno tras otro.
  - Editor: «Ocultar personaje» → «Todos los que están en escena (a la vez)» agrega un Ocultar por cada uno, seguidos.
  - La IA sabe que una pausa entre dos «ocultar» los escalona.
- **Revisar encuadre** (botón junto a ▶ Reproducir):
  - Detecta renders que terminan (o quedan) fuera del cuadro, personajes mucho más pequeños que los demás y props diminutos, con la misma geometría que la preview y VEGAS y la parte visible de cada imagen.
  - **Fuera del cuadro**: «Limitar al cuadro» conserva la dirección del movimiento (un empujón, una pelea) y solo lo acorta; «Era una entrada» (solo en la primera aparición) hace que entre desde fuera. Una salida justo antes de ocultarse no se toca.
  - **Tamaños**: todos se igualan por la altura visible (los ponies también son personas); los renders muy anchos (sentados, con muebles) con un ajuste prudente. El porcentaje de cada ajuste se puede cambiar antes de aplicarlo.
  - **Memoria por escena**: cada corrección se recuerda con los valores originales; al volver a revisar se puede cambiar de «Limitar» a «Era una entrada», deshacer o marcar «es a propósito». Si cambias el bloque a mano (u otro render), se revisa de nuevo.
  - Tras «Aplicar» del Director, avisa cuántos problemas hay y el primer ▶ de esa escena pregunta si revisarlos antes de generar la preview.
  - Editor: **«⤢ Ajustar al cuadro»** corrige los campos del bloque abierto (se ve antes de guardar; pregunta si era una entrada) y **«Escala %»** agranda o achica ancho y alto a la vez.
  - La guía de la IA explica hacia dónde animar sin salirse (hacia el centro, recorridos cortos, entradas desde fuera) y que no dé tamaños distintos a los personajes.
- **Voice Lab**: al guardar un perfil nuevo, ofrece crear el personaje con ese nombre y esa voz (o asignársela a un personaje de ese nombre que aún no tenga voz).
- **Pruebas**: 7 nuevas o ampliadas (reglas de «Aplicar», voces con escena cambiada, ocultar varios y quién está en escena, cuatro de encuadre con su memoria).
  - Pendiente en Windows: compilar la app y probar los borradores separados, «Borrar borrador», la ventana «Revisar encuadre», «Ajustar al cuadro», «Escala %» y el aviso de Voice Lab.

## v1.4.3 — «Cambiar dirección», invertir sin saltos en VEGAS y copiar el borrador aplicado
- **Nuevo «Cambiar dirección»** (Fondo, render, imagen y video, junto a «Invertir horizontal»): el render mira hacia el otro lado **sin moverse**; la imagen se voltea sobre su propio centro. Se guarda con el bloque, «Seguir desde el anterior» lo hereda y «Copiar escena» lo escribe.
- **«Invertir horizontal» es un espejo de la capa en el cuadro**: pasa al lado contrario (izquierda ↔ derecha, también el carril automático), con el desplazamiento X, el giro y la animación horizontal reflejados, y mira hacia el otro lado. Ahora la preview y VEGAS hacen lo mismo. Con las dos casillas a la vez, cambia de lado y sigue mirando hacia donde miraba.
- **VEGAS**: con el medio original (Pan/Crop nativo), invertir intercambiaba los bordes de todo el encuadre y el render saltaba al lado contrario de la escena (Bart encima de Lisa), aunque la preview lo mostraba en su sitio. Ahora el encuadre se refleja sobre el centro del propio render: giro, animación, cámara y gestos siguen donde estaban.
- **Director**: `cambiar direccion=si` (alias `direccion=si`, `dar la vuelta=si`, `mirar al otro lado=si`). La gramática de la IA explica la diferencia con `voltear h=si`.
- **Director (prompt)**: «Copiar borrador» vuelve a funcionar después de «Aplicar al guion»: solo se bloquea si el borrador está vacío.
- Las previews guardadas se regeneran (cambió cómo se dibuja «Invertir horizontal»).
- **Ojo con escenas antiguas**: un render que usaba «Invertir horizontal» (o `voltear h=si`) solo para mirar al otro lado ahora cambia de lado también en la preview. Para que se quede en su sitio, cambia esa casilla por «Cambiar dirección».
- **Pruebas**: 2 nuevas. La preview y VEGAS (Pan/Crop nativo y medio normalizado) coinciden al invertir: a la izquierda, con giro, en horizontal + vertical y con gesto. «Cambiar dirección», «Invertir horizontal» y las dos juntas quedan donde deben, también desde el Director.
  - Pendiente en Windows: compilar la app y probar las casillas y «Copiar borrador».

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

## v0.9.0-alpha hotfix 21 — Voces grabadas a prueba de accidentes
- Qué hace asignar un personaje a una toma: es una ETIQUETA (quién habla para el Director, pista por personaje en VEGAS, Diálogo en vez de Narración). Aunque ese personaje tenga voz en Voice Lab, «Generar voces de escena», la preview, «Exportar a VEGAS» y «Exportar episodio» usan siempre el WAV grabado; nunca lo sustituyen por TTS.
- «Regenerar bloque con TTS» pide confirmación indicando el perfil que usará y guarda la referencia a la grabación en el bloque. «Restaurar grabación» vuelve a la toma original en cualquier momento. Si la generación falla o se cancela, el bloque recupera la grabación.
- Editor de guion: si cambias el texto, el personaje o la voz de una toma grabada y existe un perfil TTS utilizable, pregunta si quieres regenerarla (por defecto No: solo guarda los cambios y conserva el WAV).
- Corregido: cambiar una toma de un personaje a Narrador (o al revés) en el editor borraba la referencia al WAV grabado sin avisar. Ahora la conserva; también se permite dejar sin texto una toma grabada al cambiar su tipo de voz.
- Aplicar un borrador con «Sustituir bloques actuales» sobre una escena con voces grabadas avisa de cuántas dejará de usar (los WAV siguen en el proyecto) antes de continuar.
- Las tomas regeneradas con TTS que conservan su grabación siguen contando como voces grabadas para el Director (modo voces grabadas) y el Director de episodio.
- El tooltip de «Asignar voz» del importador aclara que es opcional y solo se usa si luego regeneras con TTS.

## v0.9.0-alpha hotfix 20 — Director de episodio y exportación de episodio a VEGAS
- Director IA → «Generar episodio (varias escenas)». Historia: la IA planifica el episodio (1–12 escenas con título, lugar, personajes y resumen), pide confirmación y dirige cada escena con el mismo Director tipado, pasándole el esquema del episodio y cómo terminó la escena anterior. Voces grabadas: reparte las tomas de la escena seleccionada en escenas consecutivas (la IA elige los cortes; máximo 40 tomas por escena y 12 escenas, con partes iguales si la IA falla) y monta cada una.
- Las escenas se crean NUEVAS al final del episodio en cuanto cada una está lista: nada existente se modifica y, si cancelas, se conservan las terminadas. Las filas que no pasan la validación quedan como comentario «⚠ Revisar: instrucción — motivo»; las tomas que la IA olvide se añaden al final con aviso, así que ningún WAV se pierde. La escena con las tomas originales no se toca.
- «Exportar episodio» (junto a Exportar a VEGAS): todas las escenas del episodio seleccionado en UN proyecto VEGAS, una tras otra, con una región con nombre por escena. Las pistas con la misma clave (fondo, cada personaje en pantalla, cada voz) continúan entre escenas. Genera antes las voces TTS que falten, escena por escena.
- Nuevo selector «Audio: por personaje / Legacy» para la escena y el episodio. Por personaje: una pista «Voz · Bart», «Voz · Narrador»… en todo el proyecto (una pista «(2)» solo si dos líneas del mismo personaje se solapan); música, SFX y audio de vídeo se agrupan en el menor número de pistas con el mismo volumen y sin eventos solapados, para que VEGAS no haga fundidos automáticos. Legacy: cada audio en su pista, como antes.
- Verificado en Linux: episodio de 2 escenas exportado con FFmpeg real en ambos modos (6 pistas por personaje frente a 14 en Legacy, tiempos desplazados a la escena 2, regiones correctas); los scripts .cs generados para VEGAS 14+ y 12/13 compilan contra una API de VEGAS simulada; reparto de tomas (100, 50, 60 y 500 tomas, cortes de la IA, límites) y montaje de escena (filas rechazadas, tomas olvidadas) con pruebas; esquemas del plan compilados a gramática con llama.cpp. La App compila con stubs de WPF sin errores nuevos. Pendiente en Windows: `dotnet build`, Ollama real y abrir el script en VEGAS (la creación de regiones usa `new Region(posición, duración, nombre)`).

## v0.9.0-alpha hotfix 19 — Personajes como etiqueta en Voces grabadas
- «Voces grabadas»: el selector de personaje es editable. Elige uno o escribe un nombre nuevo y pulsa «Asignar personaje»: si no existe se crea como personaje SIN voz TTS (solo etiqueta), sin pasar por Voice Lab. Las tomas grabadas nunca necesitaron perfil de voz; Voice Lab puede añadírselo después si quieres regenerar alguna línea con TTS.
- Al añadir WAV, el personaje se reconoce por el nombre del archivo cuando coincide con uno registrado (`03_Bart_hola.wav`, `bart01.wav`, `Pinkie Pie - toma 2.wav`), sin distinguir mayúsculas ni tildes. Si el nombre menciona a dos personajes distintos, o solo contiene palabras sueltas como «al», no adivina. El estado indica cuántas tomas se reconocieron.
- Director IA en modo voces grabadas: la lista de personajes indica quién habla en las tomas (en vez de «voz sin perfil») y esos personajes reciben el mismo abanico amplio de renders que un personaje nombrado en la premisa.

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

## v0.9.0-alpha hotfix 16 — Bloques en grupo, catálogo y prompt maestro
- «Bloques de escena» admite selección múltiple con Ctrl o Shift; Tab amplía hacia abajo y Shift+Tab hacia arriba. Subir o bajar mueve cada bloque seleccionado una posición sin alterar el orden entre los seleccionados; eliminar borra toda la selección y recalcula tiempos.
- Biblioteca: filtros de texto, tipo, fuente y carpeta/subcarpeta. La búsqueda se ejecuta con Buscar o Enter fuera del hilo de la interfaz; muestra hasta 1200 filas y conserva todos los resultados para «editar filtrados». Los filtros pendientes requieren pulsar Buscar antes de editar el lote.
- Editor por lote de tipo y personaje/sujeto para recursos seleccionados o todos los filtrados. Los cambios manuales sobreviven a reescaneos sin provocar escrituras repetidas de los mismos recursos; una migración de base de datos v5 añade índices de nombre, tipo/fuente/ruta y sujeto. La actualización masiva se ejecuta en una conexión de fondo y una transacción.
- Director IA: «Prompt maestro…» muestra las instrucciones base editables y la vista previa del sistema para el modo actual. Guardar persiste la base por proyecto; restablecer recupera la versión integrada.

## v0.9.0-alpha hotfix 15 — Borradores entre escenas y copia de respaldo
- El borrador del Director manual, el montaje de voces y el Director IA permanece disponible al cambiar de escena. La escena actualmente seleccionada es el destino al pulsar «Aplicar al guion»; los bloques copiados reciben nuevos IDs y conservan sus recursos, ajustes y referencias WAV.
- Al volver a la escena original se puede aplicar el borrador si su guion no cambió. Si un destino distinto tiene bloques y el borrador lo sustituiría, se solicita confirmación antes de reemplazarlos.
- «Copiar borrador» en Director (prompt) y Director IA ofrece bloque simplificado o JSON completo. El JSON conserva también las instrucciones de la tabla y se puede pegar en Director (prompt), validar y aplicar sin repetir la generación IA.

## v0.9.0-alpha hotfix 14 — JSON de Gemini en Director y Biblioteca
- Ajusta el cuerpo de `generateContent`: `generationConfig.responseMimeType` y `responseJsonSchema` reciben los esquemas del borrador y del análisis de imágenes. La configuración anterior enviaba `responseFormat.text.mimeType`, que Gemini rechazaba con HTTP 400.
- Ollama, la selección de modelos y la exportación VEGAS conservan su comportamiento.

## v0.9.0-alpha hotfix 13 — Exportación Normal sobre medios originales
- Selector Normal / Legacy junto a la resolución en «Exportar a VEGAS». Legacy conserva la exportación de medios normalizados y los ajustes previos; cada modo usa su propia carpeta.
- Normal enlaza fondo, render, imagen, GIF y vídeo sin alfa/croma a los recursos originales. Pan/Crop del evento sitúa y escala, ajusta el aspecto al proyecto, recorta márgenes, invierte, gira, anima X/Y/rotación y respeta las divisiones por cámara y transiciones.
- Vídeos con alfa o croma marcado conservan el medio procesado con el original como segunda toma. Los recortes que no se puedan interpretar nativamente también conservan el camino Legacy para ese evento.
- Vista previa y reparación selectiva de PNG para FFmpeg continúan usando sus propios medios; Normal puede enlazar directamente el PNG que VEGAS abre aunque FFmpeg no tolere sus bytes finales.

## v0.9.0-alpha hotfix 12 — Recursos originales y PNG con datos añadidos
- El PNG aportado termina en `IEND` seguido por cuatro bytes `0D 0A 0D 0A`: FFmpeg intenta decodificar esos bytes como imágenes extra al usar `-loop 1`; VEGAS acepta el recurso directamente.
- Los PNG válidos se usan desde su ruta original sin crear copia. Solo si hay bytes después de `IEND`, se guarda en caché una copia exacta hasta el final de `IEND`; no se reescala, decodifica ni aplica ninguna edición al copiarla.
- La exportación VEGAS conserva la ruta al PNG original como segunda toma editable incluso cuando FFmpeg necesita la copia saneada para componer la primera toma.

## v0.9.0-alpha hotfix 11 — PNG compatibles y errores legibles
- Prepara una copia PNG compatible usando el lector de imágenes de Windows y la reutiliza por archivo y fecha de modificación para preview y exportación VEGAS. La biblioteca conserva el original.
- Registra el detalle de fallos capturados y no capturados en `%LOCALAPPDATA%\LoquendoAI\errores.log`. Los mensajes y la barra de estado muestran solo un resumen, sin desplazar el editor de bloques.
- Si Windows tampoco puede abrir el PNG, informa qué archivo falló y guarda la excepción completa en el log.

## v0.9.0-alpha hotfix 10 — Compilación de Configurar IA
- Corrige cuatro márgenes del selector de proveedor con el constructor WPF de cuatro valores. No altera los ajustes ni las llamadas a Gemini/Ollama.

## v0.9.0-alpha hotfix 9 — Selector de proveedor IA y Gemini
- Configuración al iniciar y botón «Configurar IA…» en Director y Biblioteca: elige Ollama local o Gemini, consulta sus modelos y asigna modelos de texto y visión por separado.
- API key de Gemini protegida para el usuario de Windows con DPAPI, fuera del proyecto y del paquete; admite GEMINI_API_KEY como alternativa. La API key se transmite únicamente en la cabecera HTTPS de las solicitudes a Google.
- Gemini genera el mismo borrador estructurado y revisable del Director IA y analiza imágenes seleccionadas; conserva el control de IDs, recursos, perfiles y validación de instrucciones antes de aplicar.
- Esfuerzo predeterminado, bajo, medio o alto para modelos Gemini 3; modelos Gemini 2.5 mantienen su ajuste propio predeterminado. Las etiquetas de imagen identifican proveedor y modelo para reutilizar análisis previos.

## v0.9.0-alpha hotfix 8 — Claves Pan/Crop compatibles con VEGAS 21
- Agrega cada nueva clave al evento antes de modificar su rectángulo, centro o interpolación. Soluciona el error «Fotograma clave de movimiento de vídeo no válido» al ejecutar el script exportado.
- Conserva la exportación PNG de imágenes fijas y las claves editables de desplazamiento y rotación de hotfix 7.

## v0.9.0-alpha hotfix 7 — Movimiento editable en VEGAS
- La exportación conserva como PNG las imágenes fijas con movimiento; GIF y vídeo permanecen MOV por sus fotogramas, pero el movimiento X/Y y giro ahora se escribe en fotogramas clave del Pan/Crop de cada evento VEGAS.
- Un fondo o vídeo de fondo animado sin recorte de cámara incluye margen visual adicional para permitir los paneos y giros nativos sin dejar bordes vacíos. Los segmentos divididos por cámara conservan el progreso correspondiente al instante de inicio.
- VEGAS sigue usando un medio normalizado para la composición inicial, escala, croma e inversiones. La preview mantiene su animación FFmpeg; abre el proyecto VEGAS generado para ajustar manualmente sus claves y el centro de giro.

## v0.9.0-alpha hotfix 6 — Movimiento de imágenes y vídeo
- Fondo, render, imagen/GIF y vídeo admiten desplazamiento X/Y y giro lineal durante una duración opcional, además de los valores iniciales fijos. Editor, Director y copia de escena comparten estos parámetros.
- Preview FFmpeg y medios normalizados para VEGAS usan el mismo avance temporal; el fondo amplía su lienzo durante los paneos y giros. Imágenes fijas animadas se exportan como MOV con alfa.
- Clips divididos por cámara continúan el movimiento desde el instante correcto; exportaciones y previews nuevas usan firmas distintas.
- VEGAS recibe la animación dentro del medio normalizado; sus pistas, solapamientos y transiciones siguen editables. El movimiento no son fotogramas clave nativos de VEGAS.

## v0.9.0-alpha hotfix 5 — Director IA con modelos locales diversos
- Modo historia: JSON con instrucciones solamente; no se solicitan IDs de bloques de WAV inexistentes. El modo voces grabadas mantiene IDs obligatorios y restringidos a los bloques originales.
- Recursos catalogados presentados a la IA con referencias cortas A1, A2…; el programa las resuelve a GUID antes de validar y mostrar el borrador. Catálogo más compacto que incluye pistas identificadas como ambientales.
- Análisis de imágenes comprueba si el modelo anuncia visión mediante Ollama; los errores de arquitectura del servidor ofrecen una explicación y un comando para comprobar el modelo sin la app.
- Temperatura controlada y preferencia inicial por un modelo Gemma 4 instalado, conservando la selección previa al recargar.

## v0.9.0-alpha hotfix 4 — Cancelación segura de etiquetas
- El doble clic de Biblioteca comprueba el elemento de tabla bajo el puntero y recorre de forma segura elementos visuales y de texto.
- El editor de etiquetas incluye «Cancelar» y sale sin guardar al pulsarlo, cerrar la ventana o presionar Esc.
- Si ocurre una excepción en la interfaz, la app muestra el error y escribe su detalle en `%LOCALAPPDATA%\LoquendoAI\errores.log`.

## v0.9.0-alpha hotfix 3 — Compilación y modelos de Ollama
- Corrige CS4014 al diferir la actualización de la tabla de assets y CS0252 al verificar la fila seleccionada.
- Los modelos del Director IA y del analizador visual se eligen en desplegables que consultan los modelos locales de Ollama; recarga manual tras instalar uno nuevo y conserva las selecciones disponibles.

## v0.9.0-alpha hotfix 2 — Edición y selección de recursos
- Las tablas de Director IA y Director por prompt permiten editar cada instrucción y revalidar sin regenerar; selector de archivo catalogado por fila.
- Etiquetas visibles en Biblioteca: edición directa de descripción, formulario para uso/ánimo/sujeto y acceso claro al análisis por lotes con Ollama visión.
- Filtrado de hojas de sprites por etiquetas y nombres, con posibilidad de corregir manualmente; la música pedida como ambiental necesita una pista identificable o una corrección explícita.

## v0.9.0-alpha hotfix 1 — Guía de sintaxis y recursos de IA
- Referencia interna completa por bloque y opciones permitidas; ejemplos de render con personaje e ID reales del catálogo.
- Valida que el ID elegido se ofreció al modelo. Recupera encuadre `medio`, render con sujeto registrado y pausas mezcladas con diálogo cuando la corrección es inequívoca.
- Muestra las correcciones en el estado del borrador; una acción ambigua sigue pendiente y no se aplica.

## v0.9.0-alpha — Primer Director IA local
- Nueva pestaña **Director IA (0.9)** para crear un borrador desde una premisa o montar una escena con los WAV incorporados y sus transcripciones.
- Resolución de recursos por identificador del catálogo, validación de perfiles y verificación de que los bloques grabados aparezcan exactamente una vez antes de aplicar.
- Etiquetas manuales de recursos y análisis opcional de imágenes y primer fotograma de GIF mediante un modelo visual de Ollama; resultados almacenados por hash, modelo y versión del analizador.
- Borrador revisable con errores visibles y aplicación transaccional al guion existente. Ollama se usa en localhost; la generación de episodios, proveedores adicionales y continuidad quedan para versiones posteriores.

## v0.0.8-alpha hotfix 1 — Cámara de escena y marcos integrados
- Primer plano adapta el recorte al largo visible del render; los renders de medio cuerpo mantienen su silueta y solo se amplían suavemente.
- La cámara recorta y escala el plano completo, con fondo y demás capas. VEGAS prepara segmentos de clips en los puntos de cambio para igualar la preview.
- Los fondos pueden quitar pares de bordes uniformes incrustados; un control del bloque permite desactivar la detección.
- Se incrementó la firma de preview y el sufijo de exportación. Quedan pendientes transiciones y decisiones automáticas de cámara según el diálogo.

## v0.0.8-alpha — Primeros presets de encuadre
- Nuevo selector de encuadre para renders: original, automático, cuerpo entero, medio cuerpo y primer plano. Los bloques previos conservan su aspecto original.
- El Director por líneas acepta presets opcionales en [MOSTRAR] y activa el modo automático en nuevos personajes.
- Análisis del contorno alfa para omitir márgenes vacíos; distribución estable de personajes entre zonas según concurrencia, con tamaño limitado por espacio disponible.
- El compositor MP4 y el puente a VEGAS comparten las mismas dimensiones, recorte y fórmula de posición. Cambian la firma del preview y la carpeta de exportación para regenerar resultados viejos.
- La clasificación automática es heurística: puede corregirse con el selector manual y los controles existentes. Aún no realiza cortes de cámara entre diálogos.

## v0.0.7-alpha hotfix 2 — Encuadre fiel y resolución VEGAS

- El selector de Preview exporta escenas a 1280×720 o 1920×1080 (16:9, 25 fps).
- El bridge genera un lienzo completo por clip visual con escala, posición, desplazamiento, inversión y croma del compositor; los fondos se recortan en modo cubrir para evitar márgenes laterales.
- PNG alfa para imágenes/fondos estáticos, ProRes 4444 para GIF/video. El medio original permanece como segunda toma de cada evento; voz, SFX y música conservan sus archivos y tiempos.
- Cada resolución se guarda en carpeta nueva para conservar proyectos `.veg` anteriores. La preview interna sigue a 720p.

## v0.0.7-alpha hotfix 1 — Alfa y croma en VEGAS

- VEGAS Bridge prepara MOV con alfa y videos marcados para croma verde como ProRes 4444 transparente mediante FFmpeg; la conversión reproduce el color y tolerancia elegidos en el compositor.
- El script marca los medios preparados como alfa Straight y ofrece el archivo fuente original como segunda toma del mismo evento.
- Se guardan las rutas originales y las rutas preparadas en `escena.json`; los demás medios siguen importándose sin conversión.
- La preparación ocurre fuera del hilo visual y conserva archivos ya preparados de la misma revisión. Los originales no se modifican.
- La exportación usa una carpeta nueva con sufijo `_hf1` para preservar el `.veg` creado por la versión inicial.

## v0.0.7-alpha — VEGAS Bridge inicial

- El panel Preview ofrece **Exportar a VEGAS**. Resuelve las voces y los assets, usa los mismos tiempos calculados para el MP4 y abre la carpeta `generated/vegas/scene_<id>_<firma>`.
- Exporta un manifiesto `escena.json` y scripts C# para VEGAS 14+ y VEGAS 12/13. Se ejecutan dentro de VEGAS sobre un proyecto vacío; allí se crea el `.veg` nativo con pistas de video y audio editables que apuntan a los medios originales.
- Mantiene simultaneidad de voces y efectos, duración de fondo, apariciones/ocultamientos, orden de capas, música a 25 %, bucles de audio y cambios de velocidad con pitch bloqueado. Crea una pista por clip para permitir solapamientos sin fundidos accidentales.
- Registra en el manifiesto el tamaño, posición, desplazamiento, inversión, alfa/croma, duración y modo de audio de cada bloque. El script aplica Track Motion inicial a personajes, props y videos superpuestos; la equivalencia visual exacta y el croma aún requieren trabajo y una prueba en VEGAS real.
- Si ya existe el `.veg` de la exportación, el script se detiene antes de modificar el proyecto. La preview y el proyecto Loquendo no se modifican.

## v0.0.6-alpha hotfix 3

- Transformación visual común en fondo, renders, props/GIF y video: tamaño máximo, desplazamiento e inversión en ambos ejes.
- Los valores previos del video siguen disponibles al abrir un proyecto creado con hotfix 2; el preview se regenera al cambiar de versión.
- Editor y reproductor señalan expresamente que Texto en pantalla y Transición aún no se renderizan.

## v0.0.6-alpha hotfix 2

- Ancho, alto y desplazamiento X/Y ajustables para videos superpuestos, con proporción conservada.
- Inversión horizontal y vertical independiente de la posición del video.
- Archivo de preview por huella de escena para evitar que Windows bloquee la regeneración al cambiar el bloque varias veces.

## v0.0.6-alpha hotfix 1

- La composición de videos superpuestos limita ancho y alto conservando la relación de aspecto: izquierda y derecha vuelven a representar posiciones distintas para MOV alfa y video con croma.
- Fondo fijo continúa llenando el fotograma y desactiva el selector de posición.
- Preview invalidado para regenerar los MP4 existentes con la corrección.

## v0.0.6-alpha

- Director inicial: instrucciones por líneas, recursos sugeridos desde la Biblioteca, borrador revisable y aplicación segura a la escena. El prompt libre asistido por IA queda para una iteración siguiente.
- El editor del bloque desplaza sus campos dentro de una región propia; Nuevo/Guardar permanece visible y el preview mantiene su barra de reproducción.
- Los 26 archivos de notas `HOTFIX_*.md` anteriores se consolidaron en `HISTORIAL_HOTFIXES.md`; las próximas revisiones se agregarán a ese historial.

## v0.0.4-alpha hotfix 1
- Corregido el caché obsoleto del selector de assets del guion después de escaneos y cambios de categoría.
- Botón ↻ y contador de assets disponibles; consultas serializadas.

# v0.0.4-alpha — Structured Script

- Episodios, escenas y bloques estructurados.
- Generación/caché de voces por escena.
- Overrides de voz por diálogo.
- Timeline lógico básico y duración real desde WAV.
- Schema v4.
- Carga perezosa de assets por tipo en Guion.

# v0.0.3-beta.2

- Corrige UTF-8 extremo a extremo entre WPF y el bridge x86; nombres como `IVONA 2 Penélope` ya no se corrompen al redirigir stdout/stderr.
- SAPI5 ya no envuelve texto en XML cuando pitch=0. Esto evita crashes en wrappers antiguos (Infovox/Acapela/SAPI compatibility engines).
- Mantiene vivo el token COM de la voz seleccionada durante toda la síntesis y libera objetos COM en un orden más conservador.
- Rate nulo ahora significa realmente "usar el valor por defecto del motor" en vez de forzar 0.
- Matching de voz: exacto primero, con fallback normalizado por acentos/puntuación.

# v0.0.3-beta.1 — Voice Lab

- Nueva pestaña Voice Lab en WPF.
- CRUD de personajes y perfiles de voz persistentes.
- Asignación de perfil base a cada personaje.
- Preview desde la aplicación usando el bridge x86.
- Loquendo TTS7 Native y SAPI5 x86 como providers iniciales.
- TTS7: soporte nativo para `ttsSetPitch`, `ttsSetSpeed` y `ttsSetVolume`; pitch/speed usan escala 0..100.
- SAPI5: pitch mediante XML SAPI, rate/volume nativos.
- Velocidad base opcional: si no se fija, se conserva el default del motor.
- SQLite schema v3 con parámetros provider-specific y columnas de override de voz por diálogo preparadas.
- Nuevo `scripts\run-voice-lab.cmd`.

# 0.0.3-alpha hotfix 6

- Confirmada síntesis WAV real mediante Loquendo TTS7 Native/Win32 con ABI `stdcall`.
- Nuevo `qa-suite`: 10 casos de texto y reporte CSV con validación RIFF/WAVE.
- Nuevo `stress`: 100/500/1000+ líneas usando workers x86 aislados y reportes CSV.
- Nuevo `controls-test`: velocidad TTS7 30/50/70; rate/volume SAPI.
- Integrado `ttsSetSpeed` nativo cuando `--rate` es distinto de 0.
- El worker nativo ya no duplica la ruta de salida en stdout.
- Scripts `.cmd` para publicar y probar sin depender de ExecutionPolicy de PowerShell.
- Pitch/volume nativos de TTS7 siguen pendientes; no se simulan.

# 0.0.3-alpha hotfix 3

## 0.0.3-alpha hotfix 5

- Corrige el ABI de Loquendo TTS7 Native: `cdecl` -> `stdcall`.
- Aplica `stdcall` de forma consistente a todos los exports usados por el provider.
- Mantiene la síntesis nativa en un worker x86 aislado.

- Isolated LTTS7 native synthesis in a nested x86 worker.
- Added flush-safe native call tracing and crash exit-code reporting.
- Switched native reader creation to the implicit LTTS7 session.
- Avoided explicit LTTS7 teardown in the short-lived native worker.

# v0.0.3-alpha hotfix 1

- Fixed x86 bridge deployment on machines that only have the x64 .NET runtime.
- Removed obsolete `Prefer32Bit`.
- Added self-contained x86 publish and test scripts.

# Changelog

## v0.0.3-alpha
- Nuevo proyecto `LoquendoAI.TtsBridge32` compilado x86.
- `scan/doctor` para inventariar SAPI5, Loquendo TTS7, BALCON, Balabolka GUI, Infovox/SpeechPad y TextAloud.
- `voices` para enumerar voces mediante SAPI5 x86, Loquendo TTS7 nativo o BALCON.
- `synth` para generar WAV mediante esos providers cuando estén disponibles.
- `serve` con protocolo JSONL stdin/stdout, pensado para que la futura UI x64 controle el worker x86 sin cargar DLL antiguas dentro del proceso principal.
- Probe de Loquendo basado en `SOFTWARE\Loquendo\LTTS7\Engine\DataPath` y `LoqTTS7.dll`.
- SpeechPad/Infovox se trata como frontend/detección; se prioriza SAPI5 o AcaTTS directo en vez de automatización de GUI.
- Sin cambio de schema de proyectos.


## 0.0.2.1

- Hotfix: imports explícitos de `System.IO` en la aplicación WPF para resolver `Path`, `Directory` y `DirectoryInfo`.

## 0.0.2

### Nuevo
- Schema SQLite v2.
- Migración automática desde schema v1.
- Fuentes de assets múltiples.
- Reglas por carpeta con herencia.
- Metadata opcional de sujeto/personaje y colección/pack.
- Escaneo incremental y hash SHA-256.
- Estados de recorte (`CutoutStatus`).
- Marcado de archivos faltantes.
- UI de configuración de fuente.
- Catálogo y búsqueda básicos.

### Compatibilidad
- Conserva los valores numéricos previos de `AssetKind`; `VisualEffect` se agrega al final.
- Los assets existentes de v0.0.1 permanecen válidos aunque todavía no pertenezcan a una fuente externa.

### Seguridad de datos
- Nunca mueve, renombra ni borra archivos de las fuentes.
- `Quitar fuente` solo elimina el catálogo correspondiente del proyecto.

## v0.0.2.2
- Reescaneos movidos fuera del dispatcher de WPF.
- Conexión SQLite dedicada para el worker de escaneo.
- Progreso limitado para no inundar la UI con decenas de miles de eventos.
- Botón Cancelar escaneo.
- Refresh de bibliotecas grandes procesado en segundo plano.
- Conteos por fuente optimizados mediante agrupación única.
## v0.0.2.3
- `Mixto` deja de comportarse como `Automático`: ahora clasifica cada archivo por su naturaleza.
- En carpetas mixtas, el contexto de subcarpetas sigue teniendo prioridad (`OST`, `sonidos`, `fondos`, `VFX`, etc.).
- Fallback mixto: GIF -> Prop, audio ambiguo -> Audio, video -> Video, fuentes -> Fuente, imagen estática ambigua -> Render.
- Nuevo `AssetKind.Audio` genérico para no inventar que un audio ambiguo es música o SFX. Se agrega al final del enum para conservar los valores numéricos existentes.
- El reescaneo reclasifica assets existentes sin volver a importarlos como duplicados.


## 0.0.3-alpha.2
- Hotfix de síntesis Loquendo TTS7 nativa: sesión explícita, síntesis bloqueante, cleanup y no descargar la DLL con estado vivo.

## 0.0.3-alpha hotfix 4
- Added isolated TTS7 ABI probe (`cdecl` vs `stdcall`).
- Documented Loquendo 7.14 Java RMI Remote API (port 1099 / `LoquendoTTSEngineServer`).

## v0.0.3-beta.3
- Added Infovox/SAPI4 fallback through BALCON and Voice Lab provider support.
- Added project-local BALCON discovery and SAPI4-specific pitch/speed handling.

## v0.0.3-beta.4
- BALCON/SAPI4 hardening after Infovox exit-code 1 failures.
- Voice Lab now asks BALCON only for the SAPI4 section when using the Infovox provider.
- Neutral SAPI4 pitch/speed (50) are no longer sent as `-p 50` / `-s 50`; some legacy engines reject those property calls.
- SAPI4 input is written as UTF-16LE (`-enc unicode`) for legacy compatibility.
- SAPI4 voice selection prefers the short engine name (`Antonio`, `Javier`, `Maria`, `Rosa`) instead of the full SpeechPad label.
- BALCON is launched with its own folder as working directory.
- Error reports include both stdout and stderr.
- Added `balcon-probe` and `scripts\tts-balcon-probe.cmd`.
- Doctor reports missing companion DLLs from the official BALCON ZIP.
## v0.0.8-alpha hotfix 20 — Tomas grabadas y copia de escena
- Importador de múltiples WAV con orden editable, selección múltiple para asignar personaje, escucha y campo de transcripción manual.
- Las tomas se copian al proyecto y se distinguen del caché TTS; generar otras voces, cambiar personaje o corregir texto conserva el audio original.
- Regeneración TTS explícita por bloque con restauración de la toma importada cuando la síntesis falla.
- Copiar una escena como esquema JSON con bloques, referencias de assets, audio, ajustes y notas; el Director puede revisar y aplicar el esquema en el proyecto.
- Las notas adicionales durante la importación se guardan en la escena. STT local pendiente para el Director IA.
## v0.0.8-alpha hotfix 21 — Elegir formato al copiar escena
- El botón Copiar escena ofrece Bloque simplificado, JSON completo y Cancelar.
- El bloque simplificado emite instrucciones legibles compatibles con el Director para fondos, renders, GIF, videos, música, SFX, diálogos, pausas y transiciones.
- Los WAV importados y los ajustes que no expresa el prompt se señalan en comentarios; el JSON sigue ofreciendo la copia completa.
## v0.0.8-alpha hotfix 22 — Selección directa TTS7 y voces al importar
- Narración permite elegir automáticamente voces TTS7 disponibles sin crear perfiles persistentes; el bloque guarda la voz directa en sus parámetros.
- La importación por lote permite asignar tanto perfiles creados como voces TTS7 a las tomas grabadas.
- La columna Personaje / asset identifica el perfil guardado de una toma importada y queda vacía para voces TTS7 directas.
- La generación y la regeneración usan la voz directa seleccionada, manteniendo intacto el WAV importado hasta que se solicite regenerarlo.

## v0.0.8-alpha hotfix 23 — Transcripción local de tomas WAV
- STT opcional mediante faster-whisper con instalación aislada, modelos e idioma configurables y CPU/GPU seleccionable.
- Transcripción por lote de nuevos WAV o voces importadas anteriormente; revisión y edición de texto antes de guardarlo.
- Las tomas existentes se actualizan en sus bloques originales sin duplicarlos ni reemplazar los archivos de audio.
- Cancelación que conserva las transcripciones ya completadas y confirmación al sobrescribir texto con una nueva transcripción.

## v0.0.8-alpha hotfix 24 — Corrección de diseño de voces grabadas
- La fila de controles STT recupera su altura natural y la tabla ocupa únicamente el espacio restante, con desplazamiento para listas largas.
- Se restauró el nombre visible «Incorporar voces a la escena» y se aclaró «Transcribir lote (sin texto)».

## v0.0.8-alpha hotfix 25 — Preparación automática del STT
- La verificación del instalador ahora importa faster-whisper sin comillas internas; el mensaje de éxito lo emite PowerShell.
- `run-loquendo-ai.cmd` prepara el entorno solo cuando falta Python aislado o la dependencia no se puede importar, y conserva el editor disponible si falla la instalación opcional.

## v0.0.8-alpha hotfix 26 — Guardado estable de transcripciones
- La celda de transcripción actualiza el estado de su fila mediante `INotifyPropertyChanged` y elimina el refresco diferido de la tabla durante la confirmación de edición.
- La confirmación de la celda y de la fila se ejecuta dentro del manejo de errores antes de guardar los bloques en el proyecto.

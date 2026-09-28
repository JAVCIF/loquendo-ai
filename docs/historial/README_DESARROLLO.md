# Loquendo AI v1.4.1

Novedades en `CHANGELOG.md` (secciones v1.4.1, v1.4.0, v1.3.0, v1.2.0, v1.1.1, v1.1.0, v1.0.1 y v1.0.0-beta.8 a beta.1) y pruebas guiadas en
`PRUEBA_1.4.1.txt` y anteriores. **Versión para distribuir**: `scripts\publicar.cmd` (exe de un archivo, carpeta portable,
zip e instalador opcional, con la **transcripción local ya instalada**: el usuario solo instala Loquendo y, si quiere SAPI4,
pone BALCON; ver CHANGELOG v1.4.0). **IA**: Ollama, Gemini, Claude y ChatGPT. **Voces**: las líneas pueden usar
cualquier voz de Loquendo TTS7, SAPI4 (Juan, Antonio vía BALCON) o SAPI5 (IVONA); Voice Lab › «Voces en los selectores…»
decide cuáles aparecen. **Fondos**: zoom hasta 300 % para desplazarlos o animarlos sin bordes ni saltos, y «↳ Seguir desde
el anterior» para continuar un movimiento. **Extras (NPC)**: `[MOSTRAR] NPC | render` y `[OCULTAR] NPC | render`; en el Editor,
«Ocultar personaje» lista también los renders sin personaje. **Guion**: los bloques se reordenan arrastrándolos en la tabla. **Diálogos**: pestaña para escribir, importar (texto, Word, Excel, CSV) o generar con IA
las líneas de un episodio, generar sus voces (NPC con cualquier voz de la lista) y exportarlas a escenas como voces grabadas. **Editor**: Cámara, Cine, Gesto y Desenfoque se ajustan también desde el Editor del
bloque; en la Preview la línea de tiempo se puede pulsar o arrastrar a cualquier punto. **Interfaz**: tema claro u oscuro en rojo y blanco (selector arriba a la derecha,
«Sistema» sigue a Windows); en Guion los paneles se reparten con divisores arrastrables y «▲ Ampliar panel» deja
el Editor / Preview / Director a pantalla completa. Los efectos de VEGAS que usa (Cortador de galletas, Desenfoque gaussiano) son
siempre los propios de VEGAS; `scripts\vegas\Diagnostico_efectos_14_o_superior.cs` muestra cuáles encuentra. **Desenfoque**: `[DESENFOQUE] fondo | suavizar | duracion=600`, `ligero`,
`quitar`; en VEGAS, Desenfoque gaussiano con los rangos animados. **Gestos**: `[GESTO] Bart | balanceo`, `rebote` o `balanceo+rebote`,
`[GESTO] habla | balanceo` (quien habla en cada línea); en VEGAS, keyframes «Rápido» de Pan/Crop con el eje en los pies. **Cámara**: `[CAMARA] Bart | zoom=1.5 | duracion=300`,
`[CAMARA] habla`, `[CAMARA] general` (ver CHANGELOG); en VEGAS son keyframes de Pan/Crop editables.
**Barras de cine**: `[CINE] mostrar | estilo=cerrado | duracion=800` … `[CINE] quitar`; en VEGAS, Cortador de
galletas con el Tamaño animado. **Pruebas automáticas**: `scripts\pruebas.cmd` (o
`dotnet run --project tests\LoquendoAI.Tests`), el código de salida es el número de fallos; con un
argumento filtra por nombre (`scripts\pruebas.cmd GeometryTests`). Las de render y VEGAS necesitan FFmpeg en el PATH.
Reparto del código y de la base de datos (esquema v7) en `docs/ARCHITECTURE.md`.

**Configurar IA** conecta Ollama, Gemini y Claude a la vez; las listas de modelos muestran los que marques de
cualquiera de ellos. Copias de seguridad de la base de datos en `<proyecto>\backups` (automáticas y con el botón
**Copia de seguridad**); **Limpiar caché…** libera espacio regenerable. Transiciones de VEGAS de tu instalación en el
Director IA (botón **Transiciones VEGAS…**), voces TTS generadas en paralelo con límite de tiempo por línea, y caché de
mediciones de audio/vídeo/imágenes. Variables de ajuste: `LOQUENDO_AI_TTS_PARALLEL`,
`LOQUENDO_AI_TTS_TIMEOUT_SECONDS`, `LOQUENDO_AI_PROBE_CACHE`.

## Configurar Ollama o Gemini

Al iniciar, **Configurar IA** permite elegir Ollama local o Gemini. También
puedes abrirla desde **Director IA** y **Biblioteca**. Consulta modelos y
selecciona uno para el Director y otro que admita imágenes para etiquetar
recursos. Con Gemini introduce tu API key en la casilla oculta o define
`GEMINI_API_KEY` en Windows. La clave guardada queda protegida por Windows
para tu usuario, fuera del proyecto; el botón de configuración permite borrarla.
Los modelos Gemini 3 admiten esfuerzo predeterminado, bajo, medio y alto;
Gemini 2.5 utiliza su esfuerzo predeterminado. No hace falta crear un agente.

Gemini recibe la premisa y el catálogo usados para generar el borrador; si
analizas imágenes, recibe solo las imágenes seleccionadas. La validación del
borrador y la decisión de aplicarlo siguen en el programa. Ollama sigue
funcionando en local. Consulta `PRUEBA_GEMINI_HOTFIX9.txt` para una prueba breve.

## Revisión de borradores y recursos

En **Guion → Director (prompt)** y **Director IA (0.9)**, edita la columna
**Instrucción (editable)** del borrador y pulsa **Validar edición**. El estado y
el bloque preparado se recalculan sin volver a llamar a la IA. Para cambiar un
fondo, render, audio, imagen o vídeo, selecciona su fila y pulsa **Elegir recurso
de fila…**: escoge un archivo de una fuente ya catalogada y luego valida.
Las filas de WAV originales del modo **Montar voces grabadas** no admiten
edición libre; así conservan el audio y la transcripción. Los esquemas JSON
copiados siguen conservando sus campos originales y se editan en el JSON.

En **Biblioteca de assets**, selecciona una fila y edita **Descripción IA
(editable)** directamente; se guarda como corrección manual. Usa **Editar
etiquetas de la selección…** o doble clic en otra columna para completar uso,
ánimo y sujeto. Para detectar imágenes automáticamente, selecciona de 1 a 50
imágenes/GIF, elige un modelo de visión del proveedor configurado y pulsa
**IA: analizar imágenes seleccionadas**.
El botón está en la fila visible bajo **Assets catalogados**. Los archivos de
audio no se analizan con el modelo visual; se etiquetan manualmente.

Para una hoja de sprites, pon **Uso = hoja_de_sprites**. El Director IA la
excluirá de su catálogo de escenas; además reconoce nombres con `spritesheet`,
`atlas` o carpeta `MINIATURAS`. Si una imagen válida cae en ese filtro, pon
**Uso = render** manualmente. Los escaneos visuales ahora intentan distinguir
entre una sola pose y un atlas de poses. Para renders sin sujeto etiquetado,
la IA también consulta carpetas con el nombre exacto del personaje y le ofrece
varios candidatos para que pueda elegir. Si pides **música ambiental** y la
pista escogida no indica ambiente en nombre, ruta o etiquetas, la fila queda
pendiente: etiquétala o elige la pista correcta desde el borrador.

**Prueba sugerida:** prepara de nuevo la escena con Bart y música ambiental;
comprueba que la hoja de sprites de `MINIATURAS` no sea elegida, selecciona una
fila de música y cambia el archivo desde la tabla. Edita el diálogo en su
instrucción, valida, aplica y genera la preview. En Biblioteca cambia una
descripción, reinicia la aplicación y confirma que sigue guardada; analiza una
imagen para comprobar que aparece una descripción automática.

## Hotfix anterior: v0.9.0-alpha hotfix 1 — Gramática del Director IA

El primer borrador podía juntar `[PAUSA] 500 y Bart: Hola` en una sola línea,
poner el GUID de un render donde debía ir el nombre de Bart o proponer `medio`
como encuadre. La guía interna ahora describe la sintaxis y las opciones de
cada tipo de bloque y muestra pares reales de personaje y render cuando
existen en el catálogo. Los recursos elegidos deben pertenecer al subconjunto
de IDs que recibió la IA.

Si la IA aún devuelve una pausa seguida de un diálogo reconocido, el borrador
los divide en **dos bloques**. También convierte `medio` en `medio cuerpo` y
recupera el personaje cuando el ID de un render ofrecido tiene sujeto registrado.
Las correcciones aparecen como **Ajustado** o **Separado** en Estado. Un render
sin personaje identificable, un recurso ajeno al catálogo o una sintaxis ambigua
siguen bloqueando **Aplicar**, para que puedas revisar el resultado antes de
incorporarlo al guion.

**Prueba:** usa la misma premisa con Bart y compara las filas **Mostrar persona**
y **Pausa** del nuevo borrador. Si se ajusta la instrucción, Estado indicará
el cambio. Confirma que **Aplicar** solo se habilita cuando todos los bloques
están listos; después genera voces y preview para comprobar el montaje.

## Versión base: v0.9.0-alpha — Director IA local

Esta versión añade **Director IA (0.9)** al editor. El primer proveedor es
**Ollama local**, conectado a `127.0.0.1:11434`. Se crean borradores para una
escena desde una premisa, o se montan los WAV que ya fueron incorporados con
**Voces grabadas**. El resultado se muestra en una tabla revisable: solo se
habilita **Aplicar borrador al guion** cuando las instrucciones, personajes,
perfiles y recursos se pudieron resolver. La aplicación se hace en una sola
operación de base de datos.

## Preparación

1. Instala e inicia Ollama en Windows. Descarga un modelo local de texto que
   admita salidas JSON estructuradas (por ejemplo, `ollama pull qwen3:8b`).
   Comprueba el nombre instalado con `ollama list`. El modelo de visión es
   opcional; si quieres etiquetar imágenes automáticamente, instala uno que
   admita imágenes y JSON estructurado (por ejemplo, `ollama pull gemma3:4b`).
2. Abre el proyecto y cataloga las fuentes en **Biblioteca de assets**. Crea
   personajes y asigna un perfil de voz a cada personaje que hablará en la
   historia. Para el modo de grabaciones, primero importa los WAV, revisa las
   transcripciones y pulsa **Incorporar voces a la escena**.
3. Si deseas un catálogo visual descriptivo, selecciona imágenes o GIF en la
   biblioteca, escribe el modelo de visión y pulsa **Analizar seleccionados**.
   Se analiza el primer fotograma de cada GIF; las etiquetas quedan guardadas
   en el proyecto y se reutilizan mientras el contenido, el modelo y el
   analizador no cambien. **Editar etiquetas** permite describir fondos,
   renders y otros recursos manualmente; las etiquetas manuales prevalecen.
   Los videos se pueden etiquetar manualmente.
4. En **Guion**, selecciona una escena y abre **Director IA (0.9)**. Indica
   el nombre exacto del modelo de texto, elige el modo y escribe la premisa,
   por ejemplo: «Bart entra en la calle, encuentra un objeto extraño y pregunta
   qué pasó. Usa música tenue y una transición solo para el fondo». Pulsa
   **Generar borrador con IA**, comprueba el estado de cada fila y luego pulsa
   **Aplicar borrador al guion**. Genera las voces y la preview mediante los
   controles de escena ya existentes.

### Dos modos de borrador

| Modo | Qué produce | Qué conserva |
| --- | --- | --- |
| Historia desde prompt | Diálogos, recursos y acciones visuales para la escena seleccionada | Los bloques existentes se mantienen salvo que marques **Sustituir bloques actuales al aplicar** |
| Montar voces grabadas | Acciones visuales y orden de los bloques de la escena | Exige que todos los bloques originales aparezcan una sola vez y conserva los WAV, transcripciones y perfiles; sustituye el orden anterior de la escena al aplicar |

El Director IA usa identificadores reales del catálogo y los verifica antes
de permitir la aplicación. Si omite un bloque original o inventa un recurso,
la tabla lo indica y la aplicación queda deshabilitada. Se limitan los lotes
visuales a 50 archivos, las escenas grabadas a 65 bloques existentes y cada
respuesta a 120 acciones. El contexto envía hasta 150 recursos priorizados
por las palabras de la premisa y sus etiquetas; en bibliotecas grandes puede
ser útil concretar los nombres o la descripción de los recursos deseados.

Por ahora se trabaja **una escena a la vez** con Ollama local. La continuidad
entre escenas, el aprendizaje de estilo, un guionista de episodios y los
proveedores de pago siguen pendientes. El analizador no extrae fotogramas de
video y el modo de voces grabadas requiere el texto incorporado previamente.

### Comprobación recomendada en Windows

```powershell
dotnet build
.\scripts\run-loquendo-ai.cmd
```

Crea una escena de prueba y genera un borrador con una premisa que use un
personaje con voz y recursos del catálogo. Aplica, genera las voces y abre la
preview. Después importa dos WAV transcritos, incorpóralos, genera un borrador
en modo **Montar voces grabadas**, aplica y verifica que ambos WAV siguen siendo
los mismos y aparecen una sola vez. Cambia de escena durante otra generación:
esta debe cancelarse sin incorporar acciones a la escena anterior.

## Versión anterior: v0.0.8-alpha hotfix 26 — Edición estable de transcripciones

Se corrigió el cierre al editar la columna **Transcripción revisable** y
guardar: la tabla ya no se reconstruye mientras WPF confirma la celda.
**Revisado** y el estado STT se actualizan directamente en la fila. La
confirmación de la celda queda dentro del manejo de errores del botón
**Incorporar voces a la escena**; si la edición no puede confirmarse se muestra
un mensaje y se conserva la tabla para corregirla.

Para probarlo, transcribe un WAV, corrige una palabra en la tabla y pulsa
**Incorporar voces a la escena** sin salir primero de la celda. Vuelve a cargar
las voces de la escena: el texto editado y la marca **Revisado** deben persistir,
y el WAV debe reproducirse igual.

## Hotfix anterior: instalación STT desde el lanzador

Ejecuta `scripts\run-loquendo-ai.cmd` como de costumbre. Si falta el entorno
de transcripción o falla la importación de faster-whisper, el lanzador ejecuta
automáticamente `scripts\stt-setup.ps1` antes de abrir la app. En los siguientes
arranques omite la instalación si la dependencia ya carga bien. Si Python o
la instalación no están disponibles, muestra un aviso y permite abrir el editor;
puedes volver a intentar la instalación posteriormente con el mismo comando.

Se corrigió además la prueba de importación al final del instalador: PowerShell
ya no pasa una expresión `python -c` con comillas internas que Windows interpretaba
incorrectamente. El aviso de pip sobre una versión nueva es informativo y no
requiere actualizar pip para usar el transcriptor.

## Hotfix anterior: controles visibles en voces grabadas

Se corrigió la distribución de altura de la pestaña **Voces grabadas**:
la tabla ahora usa el espacio disponible y desplaza sus filas internamente;
los ajustes de STT y el botón **Incorporar voces a la escena** permanecen
visibles. **Transcribir lote (sin texto)** procesa todas las tomas vacías y
**Transcribir selección** permite escoger tomas concretas. El botón de
incorporar también guarda cambios de voces ya incorporadas.

## Hotfix anterior: transcripción local de voces grabadas

El transcriptor usa **faster-whisper** en tu equipo para preparar el texto de los
WAV grabados que después podrá leer el Director IA. No envía audio a una API.
Para instalarlo, abre PowerShell en la raíz del proyecto y ejecuta:

```powershell
.\scripts\stt-setup.ps1
dotnet build
```

Necesitas Python 3.9 o posterior; el script instala las dependencias en
`worker\.venv`. La primera transcripción descarga el modelo elegido y requiere
conexión; las siguientes pueden reutilizarlo. El modelo **small** y el idioma
**es** vienen preseleccionados. Puedes cambiar a **base** si buscas menor
consumo, o **medium/large-v3** si necesitas más precisión. En **Equipo**, `auto`
intenta usar la aceleración disponible; `cpu` funciona sin CUDA. Para usar GPU
NVIDIA, faster-whisper requiere el entorno CUDA/cuDNN compatible.

En **Guion → Voces grabadas**, selecciona una escena. Añade nuevos WAV o pulsa
**Cargar voces de escena** para trabajar con tomas ya incorporadas en versiones
anteriores. **Transcribir lote (sin texto)** procesa solamente las vacías; **Transcribir
selección** permite repetir una toma y te pide confirmación antes de sobrescribir
texto. Escucha el audio, corrige cada transcripción y marca **Revisado**. Al
escribir un texto a mano también se marca como revisado. Pulsa **Incorporar
voces a la escena** para conservar texto, perfiles, personajes y las notas de
dirección. Los WAV existentes no se sustituyen, y cancelar STT deja en la
tabla las transcripciones completadas hasta ese momento.

**Prueba rápida:** importa dos voces WAV (una con texto), transcribe la que no
tiene texto, escucha y corrige una palabra, guarda. Carga de nuevo las voces de
la escena y comprueba que el texto corregido y la revisión persisten, que los
WAV originales se oyen en el preview y que no aparecen bloques duplicados.
Prueba también el botón Cancelar STT durante una transcripción larga. El
Director IA queda como siguiente fase; el texto ya está disponible en cada
bloque para que pueda usarlo.

## Hotfix anterior: voces TTS7 directas

Al abrir un proyecto, el editor consulta al bridge x86 por las voces instaladas
de Loquendo TTS7. En **Narración → Voz** aparecen como `TTS7 · Jorge`,
`TTS7 · Carlos`, etc., junto a los perfiles creados en Voice Lab. Elegir una
voz directa permite generar la narración sin crear un perfil adicional.
La elección se guarda en el bloque; no añade perfiles automáticamente a la
base de datos. Si el bridge no está disponible, los perfiles existentes siguen
funcionando y las voces directas volverán a consultarse al abrir el proyecto.

En **Guion → Voces grabadas** se añadió **Voz / perfil** y **Asignar voz**:
selecciona varias tomas y asígnales un perfil guardado o una voz TTS7 directa
antes de incorporarlas a la escena. El WAV importado seguirá sonando sin
regenerarse. Si luego pulsas **Regenerar bloque con TTS**, se usará la voz
elegida. En la columna **Personaje / asset** del guion, las voces importadas
con un perfil creado muestran el nombre de ese perfil; las voces directas
TTS7 dejan esa celda vacía.

### Comprobación rápida

1. Ejecuta `dotnet build` en Windows y abre un proyecto con el bridge TTS7.
2. Añade una narración, elige `TTS7 · Jorge` en **Voz**, guarda el bloque y
   genera la escena. Confirma que la elección persiste al cerrar y abrir.
3. Importa dos WAV: asigna en lote a uno un perfil creado y al otro una voz
   TTS7 directa. Tras importar, el primero debe mostrar el nombre del perfil
   en **Personaje / asset**; el segundo debe tener esa celda vacía. Ambos WAV
   deben oírse en el preview sin generar TTS.

## Hotfix anterior: copiar escena en dos formatos

En **Guion → Director (prompt) → Copiar escena** aparece un cuadro para elegir
**Bloque simplificado**, **JSON completo** o **Cancelar**. El bloque muestra
líneas legibles como `[FONDO] calle`, `[SFX] efecto.wav | volumen=45` y
`Bart: texto`, junto con los ajustes que entiende el Director. Puedes pegarlo
en el prompt y preparar un borrador. El JSON conserva todos los datos del
hotfix anterior, incluidas las referencias a voces grabadas. Cuando el bloque
simplificado contiene una toma importada, añade un comentario para avisar
que solo el JSON conserva el WAV al volver a aplicar la escena.

## Hotfix anterior: voces grabadas y copia de escena

En **Guion → Voces grabadas** selecciona una escena, añade uno o varios WAV,
ajusta el orden con ↑/↓, selecciona varias filas y asígnales un personaje de
una vez. El texto de cada fila es opcional: permite introducir o corregir una
transcripción para el futuro Director IA. Puedes escuchar cada toma y añadir
indicaciones que se guardarán en las notas de dirección de la escena. Pulsa
**Incorporar voces a la escena** para crear bloques de diálogo o narración.

Los WAV se copian al proyecto para que sigan funcionando aunque muevas los
originales. **Generar voces de escena** y el preview respetan esas tomas aunque
cambies el personaje o edites su texto. Si quieres sustituir una toma por TTS,
escribe el diálogo, configura el perfil de voz y pulsa **Regenerar bloque con
TTS** sobre ese bloque. Si la síntesis no se completa, se restaura la toma.
El STT local con faster-whisper se incorporará en la fase del Director IA;
esta entrega todavía no transcribe automáticamente.

En **Guion → Director (prompt)**, **Copiar escena** copia un esquema JSON con
los bloques, recursos, ajustes, voces y notas. Pégalo en el mismo cuadro y
pulsa **Preparar borrador** para comprobar que se pueden reconstruir los
bloques en el proyecto. Se comprueban las referencias de recursos y WAV antes
de habilitar **Aplicar al guion**. El esquema es editable; usa **Sustituir
bloques actuales al aplicar** si quieres reemplazar la escena en lugar de
añadir otra copia. El prompt por líneas sigue funcionando igual.

### Prueba recomendada

1. En Windows, ejecuta `dotnet build` en la raíz del paquete.
2. Crea una escena con un fondo y un render. Importa dos WAV y asígnales
   personajes distintos; deja uno sin texto. Reproduce el preview sin generar
   TTS: deben oírse las dos grabaciones en secuencia.
3. Edita la transcripción de la primera voz en el editor y reproduce de nuevo.
   La toma debe seguir siendo la importada. Luego prueba **Generar voces de
   escena** y confirma que tampoco reemplaza los WAV.
4. Pulsa **Copiar escena**, pega el JSON en el Director, prepara el borrador y
   comprueba que todos los recursos y voces figuren como «Listo». En una
   escena vacía, aplica el borrador para verificar el montaje sin duplicar
   bloques en la original.
5. Selecciona una voz importada con texto y perfil de voz, pulsa **Regenerar
   bloque con TTS** y verifica que solo ese bloque cambia de audio.

## Hotfix anterior: volumen de medios

En **Guion → Editor del bloque**, los bloques SFX, Música y Video tienen
**Volumen del recurso (%)**, de 0 a 200. El Director acepta el mismo ajuste:
`[SFX] golpe.wav | volumen=70`, `[MUSICA] ambiente.mp3 | volumen=15` y
`[VIDEO] escena.mp4 | volumen=65`. Cero silencia el recurso; 100 conserva el
nivel original y 200 duplica la amplitud. El porcentaje no modifica la voz
de los personajes ni las voces configuradas en Voice Lab.

Para mantener las escenas anteriores, cuando no se especifique volumen
el SFX queda en **100%**, la música en **25%** y el video en **0%**. Un video
con volumen mayor que cero aporta su audio al MP4 y a una pista editable
de VEGAS si el archivo contiene audio. Esa pista termina al cortar o
reemplazar el video. Un video sin audio no genera pistas fantasma.

El botón ▶ del editor escucha SFX/música con su volumen hasta 100%; la
preview de escena refleja también las amplificaciones superiores a 100%.
Al editar un bloque existente se recupera su porcentaje guardado. La
exportación a VEGAS mantiene los valores de cada recurso por separado.

### Prompt de validación

Sustituye los nombres por los recursos de tu catálogo. Para la última línea,
elige **un video con pista de sonido real**; si pruebas uno mudo, aparecerá
la imagen pero no se creará una pista de audio para ese video.

```text
[FONDO] calle
[MUSICA] ambiente.mp3 | volumen=15
[SFX] efecto.wav | esperar=no | volumen=45
[VIDEO] clip-con-audio.mp4 | capa=sobre | duracion=3000 | volumen=65
Bart: La música, el efecto y el video suenan por debajo de mi voz.
[SFX] efecto.wav | esperar=si | volumen=150
Bart: El segundo efecto se amplificó y esperé a que terminara.
```

## v0.0.8-alpha hotfix 18 — Corrección de compilación

Corrige CS8123 en `SceneComposer.cs`: la tupla vacía para un render sin
fundido ya no declara nombres de elementos que el tipo inferido descarta.
El catálogo de plugins, presets, editor, Director y prompt de prueba del
hotfix 17 se conservan. Ejecuta `dotnet build` en Windows y luego el prompt
incluido en `PROMPT_TRANSICIONES_HOTFIX17.txt`.

## v0.0.8-alpha hotfix 17 — Catálogo de transiciones de VEGAS

El catálogo incorporado se extrajo del listado de transiciones de tu instalación:
**389 plugins distintos y 1542 presets** (VEGAS, NewBlue, Sapphire, Boris y
otros). En el editor de **Transición → Cruce de capas** y en **Al entrar en un
cruce** de cada fondo, render, GIF, imagen o video, elige **Plugin del catálogo…**,
busca el plugin y selecciona un preset. Cada recurso entrante puede usar uno
distinto, y el cruce conserva la duración que marca el bloque Transición.

El Director acepta `vegas=nombre o ID | preset=nombre` en un bloque
`[TRANSICION] cruce`. Para elegir el plugin solo para un render o recurso
entrante, usa `transicion=nombre o ID | preset=nombre`, o bien
`transicion=plugin | vegas=nombre o ID | preset=nombre`.
Si omites `preset`, se usa el predeterminado o el primero del catálogo.
**Disolvente, Flash y Barrido** conservan sus alias y presets anteriores.

La preview MP4 muestra el solapamiento y el tiempo del cruce; **el efecto
propietario exacto se aplica al abrir la exportación en VEGAS**. NewBlue y
Sapphire no se renderizan dentro de FFmpeg. El script busca cada identificador
en la instalación activa, aplica el preset disponible y muestra al finalizar
los plugins o presets que falten; el fundido permanece editable. El catálogo
es una instantánea: otra versión de VEGAS puede ofrecer plugins distintos.

### Prompt para probar seis transiciones

Sustituye `calle` y `Bart` por un fondo y un personaje catalogados. Genera las
voces de la escena y exporta a VEGAS para comprobar los cuatro plugins nuevos;
en la preview verás la duración y el cruce entre los encuadres.

```text
[FONDO] calle
[MOSTRAR] Bart | centro | medio cuerpo | ancho=720
Bart: Prueba inicial de transiciones.
[TRANSICION] cruce | duracion=900 | vegas=disolvente | capas=personajes
[MOSTRAR] Bart | izquierda | primer plano | ancho=900
Bart: Este es el disolvente aditivo.
[TRANSICION] cruce | duracion=900 | capas=personajes
[MOSTRAR] Bart | derecha | medio cuerpo | ancho=660 | transicion=barrido
Bart: Ahora toca el barrido lineal.
[TRANSICION] cruce | duracion=1000 | capas=personajes
[MOSTRAR] Bart | centro | primer plano | ancho=850 | transicion=NewBlue MB Zoom | preset=Centered Zoom Blur
Bart: Primer efecto NewBlue, con su preset elegido.
[TRANSICION] cruce | duracion=1000 | capas=personajes
[MOSTRAR] Bart | izquierda | medio cuerpo | ancho=720 | transicion=NewBlue 3D Page Turn | preset=Next Page - Book
Bart: Segundo efecto NewBlue, pasando la página.
[TRANSICION] cruce | duracion=1000 | capas=personajes
[MOSTRAR] Bart | derecha | primer plano | ancho=950 | transicion=S_DissolveBlur
Bart: Ahora entró Sapphire Dissolve Blur.
[TRANSICION] cruce | duracion=1000 | capas=personajes
[MOSTRAR] Bart | centro | medio cuerpo | ancho=700 | transicion=S_DissolveFlashbulbs
Bart: Y cerramos con Sapphire Flashbulbs.
```

Los bloques `[TRANSICION] cruce` van **inmediatamente antes** del render al
que afectan. Puedes asignar otro plugin a ese render sin cambiar las demás
capas. Para probar una transición en fondo, usa `capas=fondo` seguido de
`[FONDO] otro-fondo | transicion=NombreDelPlugin`.

## v0.0.8-alpha hotfix 16 — Opciones de video y SFX en el Director

Esta entrega parte de **hotfix 15**. Los ajustes del editor de bloques de video
y SFX se pueden escribir también en el Director por líneas. Los parámetros
guardan las mismas propiedades que usa la preview y la exportación de escena.

| Control del editor | Opción del Director |
| --- | --- |
| Esperar a que termine el SFX | `esperar` o `esperar=si/no` |
| Duración del SFX, música, narración o video | `duracion=2000` (audio también admite `duracion audio=2000`) |
| Recortar/repetir o ajustar velocidad del audio | `modo audio=loop/tempo` |
| Video en fondo fijo, orden del guion o encima de todo | `capa=fondo fijo/orden del guion/encima de todo` (o `fondo/guion/sobre`) |
| Pantalla verde, color y tolerancia | `pantalla verde=si/no`, `color=00FF00`, `tolerancia=0.30` (o `croma=si/no`) |
| Posición y transformación del video | `izquierda/centro/derecha`, `ancho`, `alto`, `x`, `y`, `invertir horizontal`, `invertir vertical`, `rotacion` |
| Transición por recurso | `transicion=heredar/corte/fundido/disolvente/flash/barrido` |
| Pausa después de un bloque etiquetado | `pausa=500` (o `pausa despues=500`) |

`esperar=no` permite lanzar el siguiente bloque mientras sigue sonando el SFX;
`esperar=si` retrasa el siguiente bloque hasta que termine. `pausa` añade
tiempo al reloj incluso sin marcar esperar. Como en el editor, tamaño y
posición no se aplican al video elegido como **Fondo fijo**. Deja `duracion`
sin especificar para usar la duración de origen. `[PAUSA] 500` sigue creando un bloque
de silencio propio. Las transiciones visuales no alteran el audio del SFX.

### Prompt para comprobarlo

Usa nombres de recursos que tengas catalogados para fondo y SFX. En el
borrador, comprueba la columna **Ajustes** antes de aplicar al guion.

```text
[FONDO] calle
[VIDEO] RAYOS X 2.avi | capa=encima de todo | duracion=2000 | pantalla verde=si | color=00FF00 | tolerancia=0.30 | ancho=800 | alto=500 | x=60 | y=-15 | invertir horizontal=si | rotacion=8 | transicion=corte
[SFX] nombre-exacto-del-efecto.wav | duracion=1800 | modo audio=tempo | esperar=si | pausa despues=250
Bart: Esperé a que terminara el efecto y la pausa.
[SFX] nombre-exacto-del-efecto.wav | esperar=no | duracion audio=1800 | modo audio=loop
Bart: Este efecto puede continuar debajo de mi voz.
```

Los GIF, renders, fondos e imágenes conservan sus opciones de transformación y
transición por recurso. Este Director sigue siendo un intérprete de parámetros
explícitos: todavía no elige planos o efectos mediante IA.

## v0.0.8-alpha hotfix 15 — Selección flexible del Director

Esta entrega parte de **hotfix 13**. El Director resuelve una ruta relativa exacta
antes de inferir. Si la ruta no está catalogada, usa el nombre del archivo y
prefiere la categoría correspondiente al bloque (Fondo, Render, Prop, Video,
etc.); después considera otros archivos cuyo formato sí pueda usar ese bloque.
Si el prompt incluye extensión, solo acepta esa extensión: esto también cubre
GIF y videos catalogados como renders o efectos visuales. Los nombres parecidos
pueden autoseleccionarse; la fila **Personaje / recurso** muestra el elegido.

Cuando varios archivos comparten nombre, se escoge siempre el primero según
categoría, carpeta del personaje y ruta ordenada. La columna **Estado** muestra
las rutas alternativas (pasa el cursor para leerlas completas); escribe
`[FONDO] NombreFuente/carpeta/archivo.png` si prefieres otra. Las coincidencias
se preparan para aplicar al guion. Un archivo sin ninguna coincidencia compatible
sigue marcado pendiente.

## v0.0.8-alpha hotfix 13 — Corrección de compilación

`MainWindow.Director.cs` importa explícitamente `System.IO` para que el proyecto
WPF reconozca `Path.GetExtension`. Corrige el CS0103 que disparaba otros quince
errores de tipos en la búsqueda de archivos introducida en hotfix 12.

## v0.0.8-alpha hotfix 12 — Búsqueda de recursos del Director

**Preparar borrador** busca por nombre de archivo, con o sin extensión, y por
ruta relativa del catálogo. Así `[IMAGEN] bart05.gif` encuentra un GIF aunque
esté clasificado como render, y `[VIDEO] RAYOS X 2.avi` encuentra un video
clasificado como efecto visual. El Director comprueba el formato real antes de
asignarlo; el editor permite conservar y seleccionar esos archivos al editar
el bloque. La tabla muestra el nombre buscado aunque no haya coincidencia.

Si existen varios archivos de idéntico nombre, se indican sus rutas y el
borrador espera una ruta relativa inequívoca. Una coincidencia parecida se
muestra como sugerencia para corregir el prompt; no se asigna automáticamente.
La biblioteca debe haberse escaneado y los archivos deben figurar como presentes.

## v0.0.8-alpha hotfix 11 — Transiciones por bloque visual

Corrige los cinco errores CS0103 de `MainWindow.Director.cs` al importar el
compositor. En **Guion → Editor del bloque**, cada Fondo, Mostrar personaje,
Imagen/prop (también GIF) y Video permite elegir **Al entrar en un cruce**:
Heredar selección por capas, Corte a mitad, Fundido normal, Disolvente aditivo,
Flash suave o Barrido lineal. Cada medio nuevo puede tomar una decisión distinta
en el mismo `[TRANSICION] cruce`. Un corte reemplaza al anterior en el punto
medio; un fundido solapa ambos hasta terminar el cruce. El selector por capas
define únicamente el valor de los bloques que heredan. Sin cruce, los nuevos
bloques visuales entran por corte según sus tiempos habituales.

Director: `[FONDO] calle | transicion=flash`,
`[MOSTRAR] Bart | transicion=corte`,
`[IMAGEN] humo.gif | transicion=disolvente`,
`[VIDEO] lluvia.mp4 | transicion=barrido`. También acepta `fundido` y `heredar`.
Un efecto del bloque puede actuar aunque su tipo no esté marcado en `capas=`.
La preview y VEGAS reciben el efecto del medio entrante; el destello de un fondo
permanece detrás de los personajes. Los plugins y presets exactos de VEGAS
dependen de la instalación y aún deben comprobarse allí.

## v0.0.8-alpha hotfix 10 — Transiciones por tipo de capa

En **Guion → Transición → Cruce de capas** elige a cuáles tipos afecta:
**Fondo, Personajes, Imágenes/GIF y Videos**. Por defecto están marcados todos
para conservar el comportamiento previo. El Director admite
`[TRANSICION] cruce | duracion=800 | vegas=flash | capas=fondo` o
`capas=fondo,personajes`. Los medios nuevos de un tipo excluido se sustituyen
por un corte en el punto medio; un personaje que no recibe otro bloque Mostrar
permanece intacto durante el cambio de fondo.

La preview ahora representa Flash en la capa del fondo **debajo** de los
personajes, de modo que `capas=fondo` deja visibles los renders originales. Si
Flash también se aplica a un render/prop/video, ilumina solo ese medio y
respeta su transparencia. VEGAS exporta el plugin únicamente a los eventos
entrantes de los tipos elegidos. Sin bloque Transición, un nuevo Mostrar del
mismo personaje sustituye el anterior mediante un corte limpio.

## v0.0.8-alpha hotfix 9 — Cruce en todos los visuales

La transición **Cruce de capas** vuelve a solapar el render anterior y el nuevo
durante toda su duración, con el mismo FadeIn y el mismo efecto nativo elegido
para el fondo. También se aplica a imágenes/props, GIF y videos. La preview
atenúa el render saliente mientras aparece el entrante. El cambio de
pose de Bart puede mostrar ambas siluetas durante un fundido normal, como
corresponde a una disolución; el preset **Flash suave** mezcla ambas durante el
destello. Para un cambio sin solape de dos poses, elige **Cambio a mitad**.

Los dos renders consecutivos del mismo personaje comparten una pista; las
imágenes sucesivas comparten otra. Los videos que sustituyen a otro video activo
en su misma capa también se solapan en una pista. Los videos independientes
siguen en pistas separadas para permitir composiciones simultáneas. Fondo,
personaje, imagen y video conservan sus ajustes individuales de tamaño,
posición, inversión y rotación. Reexporta el proyecto para ver las nuevas pistas.

## v0.0.8-alpha hotfix 8 — Cruce nativo en una pista

En VEGAS, las tomas consecutivas del mismo fondo se colocan ahora en **una
sola pista** con sus eventos solapados. Solo así el solape se convierte en una
transición de VEGAS y admite el preset nativo **Flash suave** en el FadeIn del
segundo evento. También se agrupan las tomas consecutivas de cada personaje en
su propia pista. Durante un cruce de fondos, la pose anterior del personaje
termina a mitad de la transición y la nueva empieza justo allí, para evitar
que aparezcan dos Barts a la vez. El MP4 aproxima Flash con un destello blanco;
la transición nativa exacta debe comprobarse al abrir la exportación en VEGAS.

El script prueba el ID del efecto del catálogo y, si hace falta, busca el
nombre del plugin. Si falta o VEGAS rechaza el preset, muestra el motivo al
terminar la exportación. Reexporta la escena desde Loquendo: los `.veg` creados
por hotfix 7 conservan las pistas separadas anteriores.

## v0.0.8-alpha hotfix 7 — Corrección de compilación del editor

Corrige `CS0136` en `MainWindow.Composition.cs`: el tiempo de una transición y
la duración total de la escena usan ahora variables distintas. Incluye los
hotfixes anteriores.

## v0.0.8-alpha hotfix 6 — Corrección de compilación

Corrige `CS8361` en `SceneComposer.cs`: la duración del fundido se calcula antes
de interpolarla en el filtro de FFmpeg. Incluye todas las funciones del hotfix 5.

## v0.0.8-alpha hotfix 5 — Cambio a mitad y cruce de capas

En **Guion → Transición** hay dos opciones nuevas. **Cambio a mitad (negro)**
oscurece la primera mitad de la transición, corta los visuales anteriores en el
punto medio y empieza allí el nuevo fondo y render; la segunda mitad revela el
nuevo plano. **Cruce de capas** empieza los visuales nuevos al inicio de la
transición, solapa ambas versiones durante el tiempo elegido y desvanece la
capa entrante. Agrupa el nuevo fondo y los nuevos renders inmediatamente después
de la transición y antes del siguiente diálogo. Los audios siguen el reloj
normal: el diálogo siguiente comienza al terminar la transición.

Para **Cruce de capas** puedes elegir **Disolvente aditivo**, **Flash suave** o
**Barrido izquierda-derecha, borde suave**. El script exportado aplica el
efecto nativo al *fundido de entrada del evento nuevo* en VEGAS, usando los
identificadores y presets del inventario de VEGAS 21 suministrado. Si ese
efecto no existe en otra instalación, el proyecto conserva el fundido sencillo
y muestra un aviso. La preview MP4 representa el cruce como opacidad gradual;
el efecto especial se aprecia al abrir la exportación en VEGAS.

El Director reconoce `[TRANSICION] cambio | duracion=800` y
`[TRANSICION] cruce | duracion=800 | vegas=flash` (también `disolvente` y
`barrido`). Los fundidos anteriores de entrada y salida conservan su
comportamiento. El historial incluye un prompt completo de comprobación.

## v0.0.8-alpha hotfix 4 — Fundidos de escena

El bloque **Transición** permite fundidos de entrada desde negro y de salida hacia negro.
Elige el tipo y una duración de 80 a 10.000 ms. La duración mueve el inicio de los
bloques siguientes, de forma que puedes cerrar una escena, insertar otro fondo y abrir
el nuevo plano. Los fundidos afectan a la imagen; los audios mantienen su mezcla.
En VEGAS se crean como una pista superior con eventos negros y fundidos editables.
Los bloques de transición antiguos, guardados antes de este hotfix sin parámetros,
conservan su comportamiento anterior hasta que los edites y guardes.

En el Director usa `[TRANSICION] fundido salida | duracion=500` y
`[TRANSICION] fundido entrada | duracion=500`. Al final del historial hay un prompt
de prueba. La carpeta `scripts/vegas` contiene scripts opcionales para generar
`transiciones_vegas.txt` con los efectos y presets disponibles en tu VEGAS.
Tres transiciones nativas pueden asignarse a los cruces de capas desde hotfix 5.

## v0.0.8-alpha hotfix 3 — Inversión y rotación

Los bloques visuales de Fondo, Mostrar personaje, Imagen/prop (GIF incluido) y Video admiten
inversión horizontal/vertical y rotación de -180° a 180° en el editor. El Director admite
`voltear h=si`, `voltear v=si` o `invertir horizontal=si` / `invertir vertical=si`,
y `rotacion=12` (grados; positivo gira en sentido horario). Se combinan con los demás
parámetros por bloque; cada cambio se guarda y se aplica tanto a la preview como al lienzo
editable que exporta a VEGAS. Fondos y videos en modo Fondo fijo se amplían al rotar para
cubrir las esquinas; un personaje o prop conserva el alfa de la zona exterior.

Ejemplo de validación al final de `HISTORIAL_HOTFIXES.md`.

## v0.0.8-alpha hotfix 1 — Cámara compartida

El preset **Primer plano** ahora acerca la **composición completa** durante la aparición de ese personaje: fondo, personajes, props y videos visibles avanzan juntos. El plano vuelve a normal al siguiente bloque Mostrar/Ocultar personaje o cuando acaba la aparición. Un render que ya es de medio cuerpo, como el Bart de la prueba, permanece completo y recibe un acercamiento leve; uno de cuerpo entero puede recortarse moderadamente. Puedes seguir ajustando tamaño, posición y desplazamiento del bloque.

Los fondos con bandas uniformes *dentro del propio archivo* se pueden recortar automáticamente. En el editor de **Fondo**, desmarca **Quitar bordes planos del archivo** si el borde forma parte intencional del dibujo. Es una detección conservadora de pares de bandas planas; no promete identificar cualquier marco decorativo o fondo liso.

La preview y la exportación VEGAS aplican la misma cámara a los visuales. Para preservar las pistas editables, VEGAS divide los eventos visuales cuando cambia el plano; los segmentos preparados conservan sus archivos originales como tomas alternativas. Si cambias a la toma original de un video dividido, revisa manualmente su punto de entrada. La cámara aún no elige cortes por diálogo y los bloques **Transición** todavía no se renderizan.

## v0.0.8-alpha — Presets de encuadre

En **Guion → Mostrar personaje → Encuadre** puedes elegir **Original**, **Automático**, **Cuerpo entero**, **Medio cuerpo** o **Primer plano**. Los nuevos bloques de personaje empiezan en **Automático** con **Posición: auto**. Los bloques guardados anteriormente se abren en **Original**, con su posición y tamaño intactos.

Automático mide el contorno alfa del primer fotograma, descarta márgenes transparentes, estima si el render es largo o de medio cuerpo y ajusta su ancho al número de personajes visibles simultáneamente. Posición auto asigna una zona estable de la pantalla durante la escena; los controles de tamaño máximo y desplazamiento X/Y siguen disponibles para corregir cada toma. En **Medio cuerpo**, si el render es largo se recorta su parte superior; **Primer plano** acerca la toma completa y limita el recorte del personaje según su altura visible. Estas heurísticas no reconocen rostros ni distinguen de forma segura renders muy estilizados: para esos recursos conviene seleccionar el preset manual y revisar el MP4.

El MP4 y la exportación a VEGAS calculan el mismo recorte y las mismas posiciones. El exportador conserva el original como toma alternativa y anota el preset en `escena.json`. El Director por líneas también admite `[MOSTRAR] Bart | auto | medio cuerpo` y usa el encuadre automático si omites la opción. La cámara todavía no crea cortes automáticos al cambiar de hablante; esa elección corresponde a la siguiente fase del Director IA. Para probar esta entrega, crea una escena con dos bloques **Mostrar personaje**, elige posición **auto** y compara la preview con la exportación a 720p o 1080p.

## Base existente: VEGAS Bridge

## Exportar una escena a VEGAS

En **Guion → Preview de escena**, selecciona la escena, elige **720p** o **1080p** en el selector VEGAS y pulsa **Exportar a VEGAS**. Se abre una carpeta con `escena.json`, `LEEME.txt` y dos scripts. Abre un proyecto nuevo y vacío en VEGAS y ejecuta el script de tu versión desde **Herramientas → Scripts → Ejecutar script**. VEGAS guardará un `.veg` en esa carpeta. Conserva la carpeta exportada y los archivos originales de tu Biblioteca.

El script importa pistas y tiempos y prepara **todos** los recursos visuales en un lienzo 16:9 de la resolución elegida. Fondo con recorte para cubrir el fotograma, renders, props, GIF, MOV con alfa y video con croma conservan tamaño, desplazamiento, inversión y orden del compositor. Las imágenes estáticas son PNG y los clips animados son ProRes 4444 con alfa dentro de `medios_normalizados`. Cada evento mantiene el original como segunda toma. La pista de audio usa los archivos fuente originales.

La preview interna sigue a 720p; el exportador a 1080p escala todo 1,5× para conservar el encuadre. Compara los bordes y el croma con el MP4: falta validar los medios reales en VEGAS. Para cambiar posición o tamaño desde Loquendo, edita el bloque y exporta otra vez. Los valores originales de cada control quedan en `escena.json`.

En **Guion → Editor del bloque**, fondo, mostrar personaje, imagen/prop (también GIF) y video comparten tamaño máximo, ajuste X/Y e inversión horizontal/vertical. El compositor aplica estos controles en el MP4. El fondo cubre su cuadro de tamaño elegido; el video configurado como **Fondo fijo** sigue llenando la pantalla y solo permite invertirlo. Los tamaños por defecto dependen del tipo para conservar el aspecto de escenas anteriores. **Texto en pantalla** y **Transición** se guardan como instrucciones del guion y aún no se representan en el MP4.

Los videos superpuestos con alfa o croma admiten **izquierda / centro / derecha**, **ancho y alto máximos**, desplazamiento **X/Y** e **invertir horizontal/vertical**. Ancho y alto preservan la relación de aspecto; por defecto usan 960 × 700 px como límites, no como tamaño forzado. Para uno aún más grande, prueba ancho 1100 y alto 720. Guarda el bloque y pulsa Reproducir. El MP4 nuevo usa un nombre distinto, así que Windows no necesita liberar el anterior para regenerarlo. **Fondo fijo** sigue llenando la escena y no usa posición ni tamaño máximo, pero sí admite inversión.

En **Guion → Director (prompt)** escribe una instrucción por línea, pulsa **Preparar borrador**, revisa los personajes y recursos sugeridos y pulsa **Aplicar al guion**. Por defecto, los bloques nuevos se agregan al final; marca **Sustituir bloques actuales** si quieres reemplazarlos. Después reproduce en **Preview de escena** para crear el MP4. Ejemplo:

```text
[FONDO] habitación
[MOSTRAR] Bart | izquierda
Bart: Hola weyes.
[SFX] homero se cae | esperar
[OCULTAR] Bart
```

Esta primera versión del Director interpreta instrucciones explícitas y busca recursos en la Biblioteca; no llama aún a un modelo de IA. Si falta un personaje, recurso o instrucción reconocible, el borrador lo muestra y no permite aplicarlo hasta corregir el texto o catalogar el recurso. La síntesis de voz y el MP4 se generan al reproducir la escena. El editor de bloques permite desplazarse sin mover los botones Nuevo/Guardar al mostrar la miniatura.

Los cambios de todas las revisiones están en `HISTORIAL_HOTFIXES.md` y las fases siguientes en `docs/ROADMAP.md`.

---

# Loquendo AI v0.0.5-alpha — Preview y compositor básico

En **Guion → Editor del bloque** puedes escuchar el SFX o la música seleccionada con ▶, elegir si un SFX retrasa el bloque siguiente y colocar los renders de personajes a la izquierda, centro o derecha. Cambiar la casilla de un SFX guardado actualiza los tiempos inmediatamente. En **Guion → Preview de escena**, ▶ / ❚❚ reproduce el MP4 existente si la escena no cambió; si cambió, genera las voces necesarias y actualiza el MP4 antes de reproducir. También puedes mover el cabezal y abrir el archivo generado.

**Requisitos:** Windows con .NET 10 SDK para ejecutar desde fuentes; `ffmpeg.exe` y `ffprobe.exe` disponibles en PATH. El MP4 se guarda en `generated/previews` del proyecto. Los assets continúan referenciando sus carpetas originales. Consulte `PREVIEW_v0.0.5-alpha.md` para alcance y pruebas.

---

# Loquendo AI v0.0.4-alpha — Guion estructurado

La primera fase de composición ya está integrada: **Episodios → Escenas → Bloques**, generación de voces por escena, overrides por línea, caché de WAV y un timeline lógico básico.

Consulta `SCRIPT_EDITOR_v0.0.4-alpha.md` para las pruebas y el alcance. Voice Lab, TTS7 nativo, SAPI5 e Infovox/SAPI4 siguen incluidos.

---

# Loquendo AI v0.0.3-beta.2

# Loquendo AI v0.0.3-beta.1 — Voice Lab

> **Voice Lab ya está conectado al TTS real:** perfiles persistentes por personaje, Loquendo TTS7 Native + SAPI5 x86, pitch, velocidad base opcional, volumen y preview desde WPF.

## Arranque rápido

```cmd
scripts\run-voice-lab.cmd
```

También puedes publicar el bridge y ejecutar la app por separado:

```cmd
scripts\tts-publish.cmd
dotnet build .\LoquendoAI.sln
dotnet run --project .\src\LoquendoAI.App\LoquendoAI.App.csproj
```

Consulta `VOICE_LAB_v0.0.3-beta.1.md`.

## QA TTS (hotfix 6)

Después de publicar el bridge con `scripts\tts-publish.cmd`, usa:

```cmd
scripts\tts-qa-loquendo.cmd
scripts\tts-controls-loquendo.cmd
scripts\tts-stress-100.cmd
```

Si 100/100 pasa, continúa con `tts-stress-500.cmd` y `tts-stress-1000.cmd`. Consulta `TTS_QA_v0.0.3-alpha.md`.

## v0.0.3-alpha — TTS Bridge / Doctor

Se agrega un primer bridge **x86** para inventariar y probar el ecosistema de voces clásico sin acoplar todavía el Voice Lab a la UI. Incluye SAPI5 x86, acceso experimental directo a Loquendo TTS7 (`LoqTTS7.dll`), soporte para `balcon.exe` cuando exista y detección de Balabolka GUI, Infovox SpeechPad 2.2 y TextAloud. Consulta `TTS_PROBE_v0.0.3-alpha.md`.


Segunda base funcional del proyecto. Esta versión mantiene el Project Core de v0.0.1 y agrega una biblioteca de assets **multi-fuente**, pensada para colecciones reales que ya llevan años organizadas a su manera.

## Qué hay en v0.0.2

- C# / .NET 10 + WPF.
- SQLite schema v2 con migración automática desde proyectos v0.0.1.
- Varias fuentes externas por proyecto: renders, fondos, audio, video, música, etc. pueden vivir en discos/carpetas diferentes.
- Los archivos originales **no se copian, mueven ni modifican**.
- Reglas de clasificación por carpeta con herencia a subcarpetas.
- Overrides para subcarpetas específicas.
- Clasificación disponible:
  - Renders / personajes
  - Fondos
  - Efectos de sonido
  - Música
  - Videos
  - Efectos visuales
  - Memes / relleno
  - Props / objetos
  - Mixto (detectar por archivo)
  - Ignorar
  - Automático / sin definir
- Metadata opcional por carpeta:
  - personaje / sujeto
  - colección / pack / canal
- No se exige separar los renders por expresión. Una carpeta `HOMERO` con imágenes sueltas funciona tal cual.
- Atajo `No clasificadas → Renders` para colecciones como la carpeta base mostrada durante el diseño.
- El nombre de carpeta puede aplicarse en lote como personaje, como colección, o no usarse.
- Estado de recorte preparado desde ahora:
  - `Ready`
  - `NeedsCutout`
  - `IntentionallyOpaque`
  - `Unknown`
- JPG/BMP dentro de una carpeta declarada como renders se marcan automáticamente como `NeedsCutout`.
- PNG de renders inspecciona el tipo de color del PNG: si no tiene canal alpha se marca `NeedsCutout`; si tiene canal alpha se considera preparado por ahora.
- SHA-256 para cada asset nuevo/cambiado.
- Reescaneo incremental: si tamaño + fecha no cambiaron, reutiliza el hash previo.
- Archivos movidos/eliminados se marcan como faltantes en lugar de romper el proyecto silenciosamente.
- Fuentes en discos externos pueden quedar `Offline` sin que el programa marque toda la biblioteca como desaparecida.
- Una fuente puede **reubicarse** a otra carpeta/unidad conservando sus reglas y sus rutas relativas.
- Búsqueda básica en el catálogo.
- Quitar una fuente solo borra sus referencias de la DB, nunca los archivos originales.

## Flujo recomendado

1. Crea o abre un proyecto.
2. Ve a **Biblioteca de assets**.
3. Pulsa **Agregar fuente…**.
4. Elige una raíz, por ejemplo:

```text
F:\Folder\IMAGENES CHIDAS
```

5. Loquendo AI muestra las carpetas de primer nivel y propone algunas reglas por nombre (`FONDOS`, `SFX`, `MUSICA`, etc.).
6. Para colecciones donde casi todas las carpetas restantes son personajes, usa **No clasificadas → Renders**.
7. Corrige las excepciones (packs, miniaturas, resources, memes, etc.).
8. Guarda la fuente. Se escanea y se agrega al catálogo.
9. Repite con cualquier otra ubicación:

```text
D:\Loquendo\VIDEOS
E:\Audio Loquendo\SFX
F:\Renders nuevos
```

Todas coexistirán dentro del mismo proyecto.


### Qué significa `Mixto`

`Mixto` se usa cuando una carpeta o árbol contiene varios tipos de recurso a la vez. No significa "dejar sin definir": primero intenta aprovechar el contexto de la ruta y después usa un fallback seguro por tipo de archivo.

```text
.../OST para el torneo/tema.mp3      -> Música
.../Efectos de Sonido/T16.wav        -> SFX
.../Personaje/renders/pose.png        -> Render
.../GIFs/tanque-12.gif                -> Prop
.../clips/hablando.avi                -> Video
.../cualquier_cosa/audio_raro.flac    -> Audio (genérico)
```

Una regla explícita siempre gana. Por ejemplo, un GIF dentro de una carpeta marcada manualmente como `Efectos visuales` seguirá siendo un efecto visual; `Prop` es solo el fallback de una carpeta `Mixto`.

## Reglas e herencia

Ejemplo:

```text
HOMERO/                     -> Renders, sujeto=HOMERO, heredar
  normal/
  disfraces/
  viejos/
```

No hace falta crear reglas para `normal`, `disfraces` o `viejos`: heredan la regla de `HOMERO`.

También puedes crear un override:

```text
HOMERO/                     -> Renders
  memes/                    -> Memes
```

## Renders sin transparencia

v0.0.2 **no quita fondos todavía**. Solo conserva la intención para no perderla:

```text
Carlos.jpg
  tipo = Render
  cutout = NeedsCutout
```

Cuando llegue el recortador automático, el original seguirá intacto y se generará una variante derivada.

## Build

```powershell
dotnet restore .\LoquendoAI.sln
dotnet build .\LoquendoAI.sln
dotnet run --project .\src\LoquendoAI.App\LoquendoAI.App.csproj
```

Requiere .NET 10 SDK en Windows.

## Compatibilidad con v0.0.1

Al abrir un proyecto antiguo, Loquendo AI migra `project.db` hasta schema 3 y actualiza el manifiesto **solo después de que la migración de DB haya terminado correctamente**.

## Lo que todavía NO hace

- No genera thumbnails todavía.
- No extrae duración/resolución de audio/video.
- No usa IA para reconocer personajes o expresiones.
- No elimina fondos.
- Exporta una primera escena editable mediante scripts de VEGAS; no exporta Premiere ni promete aún paridad visual con la preview.

Eso es deliberado: primero queremos que la biblioteca externa y sus reglas sobrevivan colecciones grandes sin destruir nada.

## Próximo hito

v0.0.3 — Script + TTS / TextAloud.

### Nota 0.0.3-alpha.2
La ruta `loquendo7-native` ahora inicializa `default.session` explícitamente y usa síntesis bloqueante. Esto corrige instalaciones TTS7 modernas/SDK donde enumerar voces funcionaba pero sintetizar terminaba el bridge de forma prematura.


### Infovox / SAPI4 (beta.4)
SpeechPad 2.2 voices that are exposed as SAPI4 can be used through the optional BALCON provider. Loquendo AI does not bundle BALCON. Extract the **complete official BALCON ZIP** into `tools\balcon\` (not only `balcon.exe`) or set `LOQUENDO_AI_BALCON` to the executable. In Voice Lab choose **Infovox / SAPI4 vía BALCON**. Neutral pitch/speed are not forced into the legacy engine; use `scripts\tts-balcon-probe.cmd` if a voice still fails.

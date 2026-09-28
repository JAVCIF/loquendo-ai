# Arquitectura — 1.4.2

> Las secciones de más abajo («Principios», «Asset sources», «Reglas»…) vienen de v0.0.2 y siguen
> vigentes. Esta primera parte describe cómo está repartido el código hoy.

## Proyectos

```text
LoquendoAI.App (WPF)          ventanas, botones, rejillas; llama a Infrastructure
  Themes/                     Light.xaml / Dark.xaml (mismos colores con nombre, rojo y blanco) y
                              Controls.xaml (estilos de todos los controles, colores por DynamicResource);
                              ThemeManager cambia el tema en vivo y guarda tema y tamaños en ui.json
  MainWindow.BlockEffects.cs  paneles del Editor para Cámara/Cine/Gesto/Desenfoque (escriben las
                              mismas claves de BlockParameters que las líneas del Director)
  MainWindow.PreviewSeek.cs   línea de tiempo de la preview (pulsar/arrastrar, teclado)
  MainWindow.Dialogues*.cs    pestaña Diálogos: guiones en <proyecto>/dialogos/*.json (se guardan solos),
  DialogueRow.cs              tabla, voces, audio en la caché de voces, prompt maestro, IA y exportar a escenas
  MainWindow.Npc.cs           perfil de voz «NPC» del sistema (respaldo de quien no tiene perfil)
  MainWindow.DirectVoices.cs  voces directas de cualquier motor (TTS7, SAPI4, SAPI5): catálogo instalado y selector;
  VoiceSelectorWindow.cs      Voice Lab › «Voces en los selectores…» (qué voces ofrecen el Editor, Diálogos y Voces grabadas)
  MainWindow.BackgroundZoom.cs zoom del fondo y «↳ Seguir desde el anterior» en el Editor
  MainWindow.BlockDrag.cs     arrastrar y soltar bloques en «Bloques de escena» (lógica en Composition/BlockOrder)
  LocalTranscriptionClient.cs transcripción local: Python embebido (worker\python) o venv; la instala si falta
  MainWindow.MediaAnalysis.cs IA de la Biblioteca: imágenes (visión), medios por nombre en lotes, audio escuchado
                              por Gemini; selección > filtro; se detiene limpio si la API se queda sin cuota
  OpenAiDirectorClient.cs     ChatGPT (OpenAI) como proveedor, junto a OllamaDirectorClient / Gemini / Claude
  ThemedMessageBox.cs         mensajes con el tema (sustituye a System.Windows.MessageBox en toda la app)
  AppPaths.cs                 datos de la app: %LOCALAPPDATA%\LoquendoAI o «datos\» en la versión portable
        |
LoquendoAI.Infrastructure     lógica sin interfaz (se prueba sin Windows)
  Library/                    MediaAnalysis: clasificar medios, nombres sin información, lote por nombre
  Tts/                        TtsBridgeClient (puente x86) y VoiceSelector (DirectVoiceRef + qué voces muestran los
                              selectores; predeterminadas: TTS7, SAPI4 Juan/Antonio, SAPI5 IVONA; selector-voces.json)
  Dialogue/                   DialogueScript: importar texto/Word/Excel/CSV (formato recomendado y análisis),
                              exportar a texto, repartir en escenas, prompt y esquema de la IA de diálogos
  Composition/                SceneComposer (preview MP4 con FFmpeg), VegasBridge (exportación),
                              LayerGeometry (dónde cae cada capa: preview y VEGAS usan la misma),
                              CameraPath/CameraPlanner (cámara: tramos lineales que la preview
                              evalúa por fotograma y VEGAS escribe como keyframes de Pan/Crop),
                              Cinema (barras de cine: CinemaPlan decide cuándo entran, se quedan
                              y salen, y qué capas llevan el Cortador de galletas; la preview
                              dibuja las mismas barras y VEGAS pone el efecto con su tamaño animado),
                              Gesture (gestos de render: GesturePlan convierte [GESTO] en keyframes
                              «Rápido» por render; la preview gira/estira alrededor de los pies y VEGAS
                              escribe el Pan/Crop completo de cada keyframe con el eje en los pies),
                              Blur (desenfoque: BlurPlan da el nivel de cada capa en el tiempo; la
                              preview usa gblur por fotograma compensando la cámara y VEGAS el
                              Desenfoque gaussiano con claves por fotograma),
                              BlockParameters (parámetros de bloque tipados y sus valores por defecto),
                              VegasTransitionCatalog, MediaProbeCache, CharacterFraming…
  Director/                   DirectorScript (lenguaje [FONDO]/[MOSTRAR]…: interpretación y validación),
                              DirectorAiSchema (esquema tipado que responde la IA), EpisodePlanning
                              (reparto de tomas en escenas, continuidad), AudioDurationEstimate
  Persistence/                SqliteProjectRepository, DatabaseSchema (migraciones), ProjectBackup
  Projects/                   ProjectService, AssetLibraryService (escaneo), ProjectMaintenance
  Tts/                        cliente del bridge x86 de Loquendo/SAPI
        |
LoquendoAI.Core               modelos y contratos (sin dependencias)
LoquendoAI.TtsBridge32        proceso x86 aislado para los motores TTS
tests/LoquendoAI.Tests        pruebas (consola, sin paquetes): scripts\pruebas.cmd
```

## Base de datos (project.db, esquema v7)

- La versión vive en un solo sitio: `schema_info` dentro de la base. El objetivo del código es
  `ProjectManifest.CurrentSchemaVersion`; `project.loquendo.json` la copia para que una versión
  anterior del programa rechace un proyecto más nuevo en lugar de abrirlo.
- Migraciones V1…V7 en `DatabaseSchema`, en orden y dentro de una transacción, con copia en
  `<proyecto>\backups` antes de migrar. Una base más nueva que el programa se rechaza sin tocarla.
- Las lecturas del catálogo (biblioteca, selector de recursos, Director, limpieza) usan su propia
  conexión de solo lectura en un hilo de fondo: Microsoft.Data.Sqlite ejecuta los métodos «Async» de
  forma síncrona y, en la conexión principal, congelaban la ventana con bibliotecas grandes.
  Escaneos, ediciones masivas y quitar una fuente escriben en una segunda conexión (WAL).
- Búsqueda: índice FTS5 `asset_search` mantenido por triggers (ver `DatabaseSchema.SearchTriggers`).

## Principios (v0.0.2)

1. **El proyecto interno es el master.** VEGAS, Premiere y FFmpeg serán salidas, no fuentes de verdad.
2. **Core sin dependencias de proveedores.** TextAloud, modelos IA y editores entran mediante adaptadores.
3. **IA fuera del proceso principal.** El Worker Python puede reiniciarse sin tumbar la aplicación.
4. **Local-first.** Proyecto, hashes y catálogo permanecen locales salvo acción explícita del usuario.
5. **Migraciones desde el inicio.** v0.0.2 introduce schema 2 y migra schema 1 transaccionalmente.
6. **Los binarios viven en disco; SQLite guarda metadata.** No se meten PNG/WAV/MP4 gigantes dentro de la DB.
7. **Una biblioteca puede tener N fuentes.** El proyecto no supone una única carpeta raíz.
8. **La organización humana es metadata valiosa.** Las reglas por carpeta preceden a la clasificación IA.
9. **Expresión es opcional.** Un render puede conocer únicamente su sujeto/personaje.
10. **No destructivo.** Las fuentes externas jamás se reorganizan ni modifican durante el catálogo.

## Capas (plan original, v0.0.2)

```text
LoquendoAI.App (WPF)
        |
        v
LoquendoAI.Core <----- contratos/modelos
        ^
        |
LoquendoAI.Infrastructure
        |
        +---- SQLite project.db
        +---- AssetLibraryService
        +---- filesystem + SHA-256
        +---- Worker Python (HTTP localhost; futuro catálogo semántico)
        +---- TTS Provider (futuro)
        +---- VEGAS Exporter (futuro)
```

## Asset sources

```text
Project
  |
  +-- Source A: F:\IMAGENES CHIDAS
  |      +-- folder rules
  |
  +-- Source B: D:\VIDEOS
  |      +-- folder rules
  |
  +-- Source C: E:\AUDIO
         +-- folder rules
```

El asset almacena `source_id + source_relative_path`; la ruta absoluta se deriva de la fuente. Un SHA-256 sirve para detectar contenido idéntico aunque exista en ubicaciones diferentes.

## Reglas

La regla más específica gana. Si una regla tiene `IncludeSubfolders=true`, se hereda hacia abajo mientras no exista un override más específico.

La regla raíz (`relative_folder = ""`) permite declarar una fuente completa como, por ejemplo, `Videos` o `SoundEffects`.

## Cutout

`CutoutStatus` está separado de `AssetKind` porque un archivo puede ser perfectamente un `CharacterSprite` aunque todavía tenga un fondo que habrá que retirar posteriormente.

## Decisión importante

No construiremos un NLE completo. Loquendo AI será un **director/editor de decisiones** y exportará una timeline editable a VEGAS/Premiere.

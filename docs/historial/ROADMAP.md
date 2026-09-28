# Roadmap — estado a v0.0.8-alpha hotfix 1

## Hecho

- **v0.0.1 — Project Core:** proyecto versionado, SQLite, contratos y shell WPF.
- **v0.0.2 — Biblioteca de assets:** fuentes externas, reglas de carpeta, escaneo incremental, hashes y detección de faltantes.
- **v0.0.3 — Voz:** personajes, perfiles, TTS Bridge x86, SAPI5/Loquendo, Voice Lab y caché WAV.
- **v0.0.4 — Guion:** episodios, escenas, bloques y timeline lógico.
- **v0.0.5-alpha — Preview y compositor:** mezcla de voces/SFX/música, fondo, sprites, MP4 y reproductor dentro de Guion.
- **v0.0.6-alpha — Primer Director:** instrucciones por líneas, búsqueda de candidatos en Biblioteca, revisión del borrador y aplicación a una escena antes de generar el preview; controles de editor estables.
- **v0.0.7-alpha — VEGAS Bridge inicial:** exportación de escenas a scripts C# que construyen pistas editables desde los medios originales y guardan un `.veg` dentro de VEGAS; manifiesto JSON con controles de composición.
- **v0.0.7-alpha hotfix 1 — alfa y croma:** normalización sin alterar los originales, importación ProRes 4444 alfa Straight y toma alternativa con el medio original.
- **v0.0.7-alpha hotfix 2 — encuadre:** visuales preparados en lienzo 720p/1080p con dimensiones y posiciones del compositor; fondo en modo cubrir y clips originales conservados como tomas alternativas.
- **v0.0.8-alpha — presets iniciales:** recorte de transparencias, encuadres entero/medio/primer plano y distribución estable de personajes en preview y VEGAS.
- **v0.0.8-alpha hotfix 1 — cámara compartida:** primer plano mueve la imagen completa, limita la ampliación de renders parciales y detecta bandas planas en fondos.

## Próximas fases hacia un solo prompt

1. **Presets de dirección (continuación):** foco en el personaje que habla, planos compartidos, cortes y transiciones como instrucciones de escena; comparar preview y pistas VEGAS.
2. **Director IA:** convertir un prompt libre en una lista editable de acciones, cámara, diálogos y recursos. Confirmar las decisiones en un borrador antes de modificar la escena.
3. **Selector inteligente de assets:** escoger renders por personaje, encuadre, expresión y continuidad, con alternativa visible cuando no haya uno adecuado.
4. **Guionista:** escribir y ajustar diálogos y pausas según duración y objetivo de la escena.
5. **Continuidad:** seguir fondo, personajes visibles, posición y objetos a través de cambios de plano y escenas.
6. **Aprendizaje de estilo:** guardar preferencias de composición aprobadas y ejemplos aportados por el usuario; aprovecharlas en sugerencias posteriores.

## Siguiente: ampliar el Director

- **v0.0.5-alpha.x (filtro de carpetas entregado):** carpeta exacta por personaje, búsqueda de renders, inclusión opcional de subcarpetas y selección manual de carpeta sin personaje.
- **v0.0.5-alpha.x (biblioteca grande):** selector por fuentes y carpetas para todos los bloques que usan recursos, carga limitada a 150 assets, búsqueda en SQLite y consultas de lectura fuera del hilo de la interfaz.
- **v0.0.5-alpha.x (selección directa y composición):** elegir cualquier archivo catalogado aunque no aparezca entre los primeros 150; miniatura del visual elegido e imagen/prop colocada en el MP4 hasta el siguiente bloque de ese tipo.
- **v0.0.5-alpha.x (duración y video):** duración visual opcional de fondos, personajes, GIF y props, sin desplazar la voz; video como fondo o capa sobre personajes con canal alfa o croma verde configurable.
- **v0.0.5-alpha.x (audio y orden de capas):** duración de narración, SFX y música con recorte, repetición o cambio de velocidad; los videos nuevos siguen el orden del guion, con opciones de fondo fijo o capa superior. Autodetección de croma en videos antiguos cuyo nombre indica pantalla verde.
- **v0.0.6-alpha.x (validación pendiente):** confirmar en Windows los tiempos de SFX, audio, GIF y MOV con alfa reales, pantalla verde y caché de preview con escenas reales; agregar galería de expresiones y controles para más capas.
- **v0.0.6-alpha.x (encuadre visual, en marcha):** tamaño máximo, desplazamiento fino X/Y e inversión horizontal/vertical disponibles en fondos, renders, props/GIF y videos desde hotfix 3. Faltan recorte/encaje/estiramiento explícitos y una interfaz de arrastre para encuadrar.
- **v0.0.6-alpha.x (bloques visuales pendientes):** Texto en pantalla y Transición siguen conservados en el guion y se anuncian como no renderizados; definir controles de estilo/duración y componerlos en MP4 antes de considerarlos disponibles.
- **v0.0.6-alpha.x (prompt libre):** incorporar un proveedor de IA local o remoto configurable para transformar descripciones libres en instrucciones del Director, con elección editable de candidatos, ambigüedad explícita y revisión antes de aplicar. La versión actual interpreta líneas explícitas y no usa IA.
- **v0.0.7-alpha.x (validación VEGAS):** ejecutar el script sobre VEGAS 12/13 o 14+ real y comparar el `.veg` con el MP4: dimensiones, posiciones, inversions, alfa, croma, bucles GIF/video, velocidad de audio y orden de capas. Ajustar el bridge a la versión concreta disponible.

## Más adelante

Ajustes manuales ligeros de timeline y capas, transiciones y exportación interoperable adicional. Para llegar al flujo de un solo prompt, el director convertirá instrucciones en un borrador de bloques, resolverá voces y recursos contra el catálogo, permitirá revisar la escena y luego usará el mismo compositor y reproductor. La aplicación dirigirá el montaje y conservará su proyecto como fuente de verdad; no busca replicar un editor de video completo.

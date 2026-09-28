# Cómo contribuir a Loquendo AI

¡Gracias por querer ayudar! Esta guía explica cómo reportar un problema, proponer una idea y enviar código para que
la revisión sea rápida y nada se pierda por el camino. Todo el proyecto, código incluido, se discute **en español**.

- [Antes de empezar](#antes-de-empezar)
- [Reportar un error (issue)](#reportar-un-error-issue)
- [Proponer una mejora](#proponer-una-mejora)
- [Enviar un pull request](#enviar-un-pull-request)
- [Preparar el entorno](#preparar-el-entorno)
- [Convenciones del código](#convenciones-del-código)
- [Pruebas](#pruebas)
- [Lista de comprobación del PR](#lista-de-comprobación-del-pr)
- [Seguridad y datos privados](#seguridad-y-datos-privados)
- [Licencia de las contribuciones](#licencia-de-las-contribuciones)

## Antes de empezar

- Busca en los [issues](../../issues?q=is%3Aissue) si ya existe algo parecido. Si existe, suma tu información ahí en
  lugar de abrir otro.
- Sé amable y concreto. Critica el código, no a las personas. No se aceptan insultos, acoso ni contenido
  discriminatorio en issues, PR ni comentarios.
- **No subas voces, renders, fondos, música ni videos con derechos de autor**: ni al repositorio ni como adjuntos de
  prueba. Si hace falta un ejemplo, usa imágenes o sonidos generados, propios o con licencia libre, e indica cuál.

## Reportar un error (issue)

Usa la plantilla **«🐞 Reporte de error»** en [Nuevo issue](../../issues/new/choose). Un buen reporte incluye:

1. **Versión** de Loquendo AI (arriba a la izquierda en la app, por ejemplo `v1.4.2`) y si es **instalada, portable o
   compilada** desde el código.
2. **Windows** (10/11, 64 bits) y, si tiene que ver con voces, **qué motores** tienes: Loquendo TTS 7, SAPI 5 (cuáles)
   o SAPI 4 con BALCON.
3. **Pasos exactos** para reproducirlo: 1, 2, 3… Cuanto más pequeño el caso, mejor. Por ejemplo: «escena nueva con un
   fondo, un render y una línea».
4. **Qué esperabas** y **qué pasó**.
5. **Registro de errores**: el final de `errores.log`, que está en
   - `%LOCALAPPDATA%\LoquendoAI\errores.log` en la versión instalada, o
   - `datos\errores.log` junto al `.exe` en la portable.
6. Si es un problema **visual** (preview o VEGAS): una captura, y el texto de **Guion → Director (prompt) → Copiar
   escena**. Es la forma más fácil de reproducir tu escena sin tus archivos.
7. Si es de **IA**: proveedor y modelo, la premisa y el estado de las filas del borrador. **Nunca** pegues tu API key.

> Un error por issue. Si encuentras tres cosas distintas, abre tres issues: así se pueden cerrar por separado.

## Proponer una mejora

Usa la plantilla **«✨ Idea o mejora»**. Cuenta:

- **El problema** que resuelve, en tus palabras: «para hacer X tengo que…».
- **Cómo te lo imaginas**: la pestaña, el botón, o la línea del Director (`[CAMARA] …`).
- **Alternativas** que probaste.
- Si afecta a la preview **y** a VEGAS, a la IA o al formato del proyecto. Así se sabe el alcance.

Para cambios grandes (un tipo de bloque nuevo, cambios en la base de datos, otro motor de voz o de IA), **abre primero
un issue** y acordemos el enfoque antes de escribir mucho código.

## Enviar un pull request

1. Haz un **fork** y crea una rama desde `main` con un nombre descriptivo:
   - `fix/preview-render-sin-personaje`
   - `feat/bloque-texto-en-pantalla`
   - `docs/readme-vegas`
2. Haz cambios **pequeños y enfocados**: un PR resuelve una cosa. Separa las refactorizaciones de los cambios de
   comportamiento.
3. Escribe commits claros en español, en imperativo, con el porqué si no es obvio:
   ```text
   Corrige el recorte del fondo con zoom al exportar a VEGAS

   El medio normalizado usaba el lienzo de movimiento como caja y ampliaba el fondo.
   Ahora usa LayerGeometry.FillExtent, igual que la preview.
   ```
4. Pasa las pruebas (`.\scripts\pruebas.cmd`) y prueba a mano lo que tocaste.
5. Abre el PR contra `main`, rellena la plantilla y enlaza el issue (`Cierra #123`).
6. Responde a la revisión con commits nuevos; no hace falta reescribir el historial. Al aceptarlo se hace
   *squash merge*.

### Qué se revisa con más cuidado

- **Compatibilidad de proyectos**: un proyecto de una versión anterior tiene que abrir y verse igual. Los cambios en
  la base de datos van como migración en `DatabaseSchema` (sube el número de esquema) y nunca borran datos.
- **Preview = VEGAS**: cualquier cambio de posición, tamaño o tiempo pasa por `LayerGeometry` / `SceneComposer` y
  tiene que dar lo mismo en la preview MP4 y en la exportación a VEGAS. Hay pruebas que comparan ambas.
- **La caché de voces**: no cambies sin motivo lo que entra en el hash de una voz, porque obligaría a regenerar todas
  las líneas de todos los proyectos.
- **La IA nunca inventa recursos**: todo lo que proponga se valida contra la biblioteca real antes de aplicarse.

## Preparar el entorno

- Windows 10/11 de 64 bits.
- [SDK de .NET 10](https://dotnet.microsoft.com/download).
- [FFmpeg](https://ffmpeg.org/download.html) en el PATH. Hace falta para la preview y para las pruebas de render; sin
  él esas pruebas se omiten.
- Opcional: Visual Studio 2026 o Rider; Loquendo TTS 7 o voces SAPI para probar voces; BALCON para SAPI 4
  (<https://www.cross-plus-a.com/es/bconsole.htm>); VEGAS Pro para probar la exportación; Ollama para la IA local;
  [Inno Setup 6](https://jrsoftware.org/isdl.php) para el instalador.

```powershell
git clone https://github.com/<tu-usuario>/loquendo-ai.git
cd loquendo-ai
.\scripts\run-loquendo-ai.cmd     # publica el puente TTS x86, compila y abre la app
.\scripts\pruebas.cmd             # pruebas
.\scripts\publicar.ps1 -Instalador   # paquetes como los del release (tarda: descarga Python, Whisper y FFmpeg)
```

El puente TTS (`LoquendoAI.TtsBridge32`) **tiene que ser x86**, porque Loquendo TTS 7 y la mayoría de voces SAPI son
de 32 bits. La app es x64 y se comunica con él por proceso.

## Convenciones del código

- **C# moderno**: `namespace` de archivo, `nullable` activado, `record` para datos, `async`/`await` sin bloquear la
  interfaz. Las operaciones largas se pueden cancelar.
- **La lógica va en `LoquendoAI.Infrastructure`**, sin dependencias de WPF, para poder probarla. `LoquendoAI.App`
  solo contiene ventanas, controles y la unión entre ambas.
- **Los parámetros de un bloque se leen y escriben con `BlockParameters`**, con valores por defecto y rangos en un
  único sitio. Si añades una opción, añádela también al lenguaje del Director (`DirectorScript`), a «Copiar escena» y,
  si aplica, al esquema de la IA (`DirectorAiSchema`).
- **Textos de la interfaz en español** y explicativos: el usuario tiene que entender qué pasó y qué puede hacer. Los
  mensajes usan `MessageBox` del proyecto, que sigue el tema.
- **Comentarios en inglés** y centrados en el **porqué**, no en el qué. Documenta con `///` las clases y los métodos no
  obvios, indicando la versión en la que se añadió un comportamiento (por ejemplo `(1.4.1)`).
- **Temas**: nada de colores fijos. Usa los recursos `Theme.*` con `DynamicResource`, para que todo funcione en claro
  y en oscuro.
- **Sin dependencias nuevas** sin hablarlo antes en un issue. Si hacen falta, tienen que ser compatibles con MIT y
  figurar en `THIRD_PARTY_NOTICES.md`.
- **No subas archivos generados**: `bin/`, `obj/`, `artifacts/`, `.venv`, cachés ni proyectos de prueba. El
  `.gitignore` ya los excluye.
- `CHANGELOG.md`: añade tu cambio a una sección «Sin publicar» al principio. La versión (`Directory.Build.props`) la sube
  quien publica.

## Pruebas

- Las pruebas son una aplicación de consola sin dependencias externas: `tests/LoquendoAI.Tests`. Cada prueba es un
  método `[Test("descripción en español")]`.
- `.\scripts\pruebas.cmd` las ejecuta todas y devuelve como código de salida el número de fallos.
  `.\scripts\pruebas.cmd Nombre` ejecuta solo las que coinciden.
- **Toda lógica nueva o corregida en Infrastructure lleva su prueba**, sobre todo la geometría, los tiempos, el
  lenguaje del Director, la importación de diálogos y las migraciones.
- La interfaz WPF no tiene pruebas automáticas: describe en el PR **cómo la probaste a mano**. Si es un cambio grande,
  añade una guía en `docs/pruebas/PRUEBA_<versión>.txt`.

## Lista de comprobación del PR

- [ ] Compila sin errores ni advertencias nuevas (`dotnet build`).
- [ ] `.\scripts\pruebas.cmd` pasa, y añadí pruebas para la lógica nueva.
- [ ] Lo probé en la app. Si es visual: preview **y** exportación a VEGAS.
- [ ] Los proyectos existentes siguen abriendo y viéndose igual, o el PR explica la migración.
- [ ] Textos en español y sin colores fijos (tema claro y oscuro).
- [ ] Documentación al día: `README.md`, `CHANGELOG.md` y, si aplica, el lenguaje del Director.
- [ ] Nada con derechos de autor ni datos personales o claves en el diff.

## Seguridad y datos privados

- **Nunca** publiques API keys, ni en issues, ni en capturas, ni en logs. Si se te escapó una, revócala en el panel del
  proveedor enseguida.
- Si encuentras una vulnerabilidad (por ejemplo, un modo de hacer que un proyecto o un archivo de diálogos ejecute
  algo), **no abras un issue público**. Usa
  [«Report a vulnerability»](../../security/advisories/new), en la pestaña *Security* del repositorio.

## Licencia de las contribuciones

Al enviar un pull request aceptas que tu contribución se publique bajo la [licencia MIT](LICENSE) del proyecto.
Confirmas también que tienes derecho a aportarla: el código es tuyo, o viene de una fuente compatible con MIT y lo
indicas.

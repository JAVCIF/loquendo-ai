# Preview de escena — v0.0.5-alpha

## Qué hace

- Audio: mezcla WAV de voz, SFX y música con FFmpeg. El SFX arranca en el tiempo actual y puede sonar simultáneo al siguiente bloque o retrasarlo hasta que termine. La música arranca donde está su bloque, se repite hasta el final y se mezcla al 25 % del volumen original. FFprobe mide los SFX y archivos de audio antes de planificar.
- Visual: fondo activo y renders de personajes en posiciones izquierda, centro o derecha. Mostrar reemplaza el render anterior del mismo personaje; Ocultar lo retira. Composición a 1280 × 720, 25 fps, H.264/AAC.
- Preview: reproducción embebida del MP4 con Play/Pausa, Stop, barra para saltar y Abrir video. El proyecto conserva los WAV de voces y regenera la composición cuando el guion o los assets cambian.
- Se guardan los tiempos calculados en la base del proyecto; el MP4 se guarda en `generated/previews/scene_<id>.mp4`.

## Prueba manual en Windows

1. Verificar que `ffmpeg -version`, `ffprobe -version` y `dotnet --version` funcionen desde la misma terminal donde se ejecutará la app.
2. Crear proyecto, catalogar un fondo PNG/JPG, renders PNG con transparencia, un SFX WAV/MP3 y música. Crear una escena, dos diálogos, fondo, dos personajes, un SFX y música.
3. En el editor seleccionar un SFX y escuchar con ▶. Guardarlo sin Esperar y pulsar ▶ / ❚❚ en Preview: el sonido se superpone al diálogo siguiente. Marcar Esperar en ese SFX guardado: el tiempo del siguiente diálogo debe actualizarse inmediatamente, antes de pulsar ▶ / ❚❚ otra vez.
4. Cambiar la posición del render, guardar, pulsar ▶ / ❚❚ y comprobar el nuevo MP4. Cambiar un diálogo y pulsar ▶ / ❚❚: debe sintetizar solo la voz necesaria y actualizar la escena.
5. Reproducir, pausar, mover la barra, detener, volver a reproducir sin regenerar y usar Abrir video. Reabrir el proyecto: ▶ / ❚❚ debe recuperar el MP4 guardado si la escena no cambió.

## Alcance de esta alpha

Los bloques Imagen, Video, Texto y Transición permanecen en el guion y aún no participan en este compositor. No hay ajustes finos de volumen ni keyframes; el editor de timeline y la exportación a VEGAS vienen después. Cada bloque Música inicia otra pista de música en la mezcla; si se colocan varias, se superponen. FFmpeg y ffprobe no están incluidos en el ZIP. El reproductor embebido depende de los códecs de video disponibles en Windows.

**Verificación de esta entrega:** se validó el XML XAML y se probó una orden FFmpeg equivalente con un fondo, un render y una voz; no fue posible compilar ni ejecutar WPF aquí por falta de .NET SDK y Windows. Ejecuta `dotnet build .\LoquendoAI.sln` en tu PC antes de usarla.

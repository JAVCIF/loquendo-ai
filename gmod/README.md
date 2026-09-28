# Loquendo Studio (addon de Garry's Mod) · prototipo 0.1

Diseñador de poses, animaciones en el sitio con un botón, lipsync desde un `.wav` y render con **transparencia
real** (sin chroma), pensado para crear personajes para Loquendo AI.

## Instalar

1. Copia la carpeta `loquendo_studio` en `...\GarrysMod\garrysmod\addons\`.
2. Entra a una partida (un jugador, cualquier mapa).
3. Ábrelo con la consola (`loquendo_studio`) o desde el menú C → **Loquendo Studio**.

## Qué hace

| Pestaña | Para qué |
| --- | --- |
| **Modelo** | Carga cualquier `.mdl` (o tu modelo de jugador) y lo **analiza**: huesos, expresiones, qué articulaciones reconoce, qué acciones puede hacer y cómo hará el lipsync. |
| **Animación** | Acciones en el sitio: respirar, hablar, caminar, correr, saltar, saludar, asentir, negar, celebrar, señalar y encoger los hombros. Se ajustan velocidad, intensidad y duración. Si el modelo trae su propia animación (modelos de jugador), puede usarla. |
| **Pose** | Congela la acción en un instante y ajusta cada articulación (adelante/atrás, lado, giro) y las expresiones faciales. Los ojos pueden mirar a la cámara. |
| **Voz** | Lipsync: elige un `.wav` de `garrysmod/data/loquendo_studio/audio/` y la boca se abre con el volumen de la voz. Sirven directamente las voces que genera Loquendo AI. |
| **Render** | Imagen PNG de la pose o la animación completa, a 1080p, vertical o cuadrada, de 24 a 60 fps, con bordes suaves opcionales. |

### Cómo se adapta a cada modelo

- **Articulaciones:** reconoce los huesos por su nombre, sea ValveBiped (HL2 y modelos de jugador), Mixamo o Blender. Las acciones no usan ángulos fijos: dicen **hacia dónde apunta** cada parte («el brazo hacia abajo y un poco adelante»), y el addon calcula el giro de cada hueso según su pose de reposo (T, A o de pie). Por eso la misma acción sirve para esqueletos distintos.
- **Huesos que faltan:** si al modelo le falta alguno, la acción hace lo que puede y el análisis la marca como «limitada».
- **Lipsync:** usa la expresión de la boca (`jaw_drop` y similares) y, si el modelo no la tiene, el hueso de la mandíbula.

## De PNG a video

Cada fotograma se guarda **sobre negro y sobre blanco**. La diferencia entre ambos da un alfa exacto: bordes, pelo y cristal incluidos, sin chroma ni restos de verde. Para obtener el archivo final:

```powershell
.\componer.ps1 "...\garrysmod\data\loquendo_studio\renders\personaje"
```

- **1 fotograma** → `personaje.png` con transparencia, para usarlo como render en Loquendo AI.
- **Animación** → `personaje.mov` en ProRes 4444 con alfa y la voz. VEGAS lo lee con transparencia; si no, en las propiedades del medio pon el canal alfa en «Directo».

Hace falta FFmpeg: el que trae Loquendo AI (`tools\ffmpeg\ffmpeg.exe`, con `-FFmpeg`) o uno en el PATH.

## Limitaciones de este prototipo

- Está escrito sin haberlo ejecutado dentro de Garry's Mod: es la primera prueba real.
- Los modelos con nombres de huesos genéricos (`bone_012`) no se reconocen todavía. Falta poder asignarlos a mano.
- El lipsync va por volumen, no por fonemas: abre y cierra bien, pero no distingue «a» de «u».

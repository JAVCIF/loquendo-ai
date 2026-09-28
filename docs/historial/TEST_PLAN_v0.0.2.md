# Prueba rápida — Loquendo AI v0.0.2

## 1. Build

```powershell
dotnet restore .\LoquendoAI.sln
dotnet build .\LoquendoAI.sln
```

## 2. Migración

Abre con v0.0.2 el mismo proyecto creado con v0.0.1.

Esperado:
- abre sin perder datos;
- muestra `schema v2`;
- `project.db` conserva las tablas anteriores y agrega `asset_sources` / `folder_rules`.

## 3. Fuente principal de renders

Agrega una carpeta raíz con muchas subcarpetas.

Prueba:
- `FONDOS` sugerida como Fondos;
- carpeta de SFX sugerida como Efectos de sonido;
- usa `No clasificadas → Renders` para las demás;
- deja `Nombre carpeta → personaje` para carpetas por personaje;
- en packs/canales cambia a colección o borra el sujeto.

Esperado:
- cada archivo aparece en la tabla de assets;
- no se mueve ningún archivo original;
- las imágenes JPG/BMP clasificadas como render aparecen con fondo `Pendiente`;
- PNG sin canal alpha también aparece `Pendiente`.

## 4. Múltiples fuentes

Agrega después otra raíz de videos y otra de audio.

Esperado:
- las tres fuentes coexisten;
- cada una tiene sus propias reglas;
- buscar encuentra assets independientemente de la fuente.

## 5. Reescaneo

Sin cambiar nada, pulsa `Reescanear`.

Esperado:
- la mayoría debe aparecer como `sin cambios`;
- no recalcula SHA-256 cuando tamaño y fecha son iguales.

Agrega un archivo y vuelve a escanear.

Esperado:
- aparece como `+1 nuevo`.

Borra/mueve un archivo y vuelve a escanear.

Esperado:
- queda catalogado como `Faltante`.

## 6. Disco externo

Desconecta temporalmente una unidad que contenga una fuente y vuelve a abrir el proyecto.

Esperado:
- la fuente aparece `Offline`;
- los assets existentes no son marcados masivamente como faltantes solo porque la unidad no está conectada.

Vuelve a conectar la unidad y usa `Reescanear`.

## 7. Reubicar

Selecciona una fuente y usa `Reubicar…` para apuntarla a otra carpeta con la misma estructura relativa.

Esperado:
- conserva las reglas;
- vuelve a encontrar los archivos por sus rutas relativas;
- no crea una fuente adicional.

## Hotfix 0.0.2.3 — Mixto

Marca una carpeta heterogénea como `Mixto (detectar por archivo)` y reescanea. Verifica al menos:

- `*.png` sin una pista más específica -> Render.
- `*.gif` sin una pista más específica -> Prop.
- `*.avi`, `*.wmv`, `*.mp4` -> Video.
- audio bajo una ruta `OST`, `Música` o `BGM` -> Música.
- audio bajo `Efectos de Sonido`, `SFX`, `sonidos` -> SFX.
- audio ambiguo -> Audio.
- un GIF bajo una regla explícita `Efectos visuales` debe seguir como Efecto visual.
- un PNG bajo una regla explícita `Fondos` debe seguir como Fondo.
- los assets previamente `Sin definir` deben actualizar su categoría al reescanear, sin duplicarse.


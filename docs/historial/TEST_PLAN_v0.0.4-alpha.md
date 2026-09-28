# Test plan v0.0.4-alpha

1. Ejecuta `scripts\run-loquendo-ai.cmd`.
2. Abre un proyecto v3 existente y confirma que pasa a schema v4 sin perder assets, personajes ni perfiles.
3. En **Guion**, crea un episodio y dos escenas.
4. Añade dos diálogos alternando dos personajes con perfiles de voz guardados.
5. En una línea fija un override de velocidad y deja las demás heredadas.
6. Añade una pausa de 500 ms, un Fondo y un SFX.
7. Reordena dos bloques con ↑/↓ y confirma que el orden persiste al reabrir el proyecto.
8. Pulsa **Generar voces de escena**. Deben aparecer WAV en `generated\voices\episode_001\scene_001\`.
9. Confirma que las líneas muestran duración y tiempo de inicio.
10. Ejecuta de nuevo **Generar voces de escena** sin editar nada: debe informar WAV reutilizados.
11. Cambia solo una línea y genera otra vez: solo esa línea debe regenerarse.
12. Usa **▶ Audio** sobre un diálogo generado.

Resultado esperado: el proyecto conserva guion estructurado, voces y timing básico sin depender de una API externa.

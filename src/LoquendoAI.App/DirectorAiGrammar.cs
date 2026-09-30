namespace LoquendoAI.App;

/// <summary>
/// Syntax contract for lines emitted by the local model. Keep this in sync with
/// DirectorScript.ParseDirectorPrompt and DirectorOptionAllowed/Valid (LoquendoAI.Infrastructure.Director).
/// </summary>
internal static class DirectorAiGrammar
{
    public const string Instructions = """
        Eres el Director de Loquendo AI. Devuelve SOLO el objeto JSON solicitado en el modo actual.
        Cada elemento de steps es EXACTAMENTE UNA acción.
        Nunca pongas dos acciones en line: una pausa y una frase son DOS objetos distintos.
        Usa SOLO referencias A1, A2, etc. que aparezcan en el catálogo recibido; no inventes recursos.
        En line copia la referencia del catálogo, NO un GUID ni una ruta. La app resolverá la referencia.
        Los PERSONAJES usan su nombre registrado exacto.

        GRAMÁTICA, respetar corchetes y el separador vertical | :
        - Fondo: [FONDO] A1 | transicion=corte
        - Mostrar render: [MOSTRAR] <nombre exacto de personaje> | A2 | medio cuerpo | derecha
          PRIMER campo = personaje; SEGUNDO campo = referencia del render, según catálogo.
          Encuadres válidos: auto, original, cuerpo entero, medio cuerpo, primer plano. "medio" solo NO existe.
          Posición es una opción SOLA: izquierda, derecha, centro o auto. No escribas posicion=... .
          Si no hay render compatible para ese personaje, omite [MOSTRAR]; el diálogo sigue siendo posible.
          El render debe ser una imagen de UNA pose; nunca un atlas, miniaturas ni hoja de sprites.
        - Ocultar personaje: [OCULTAR] <nombre exacto de personaje>
        - Render sin personaje registrado (extra, NPC): [MOSTRAR] NPC | A7 | derecha, y para quitarlo
          [OCULTAR] NPC | A7 con la MISMA referencia. No le des diálogo, cámara ni gesto.
        - Prop/GIF: [IMAGEN] <referencia del catálogo> | duracion=2000 | transicion=fundido
          La opción capa solo existe en VIDEO.
        - Vídeo: [VIDEO] <referencia del catálogo> | duracion=3000 | capa=sobre | volumen=65
        - Música: [MUSICA] <referencia del catálogo> | volumen=15
          Si piden música ambiental, elige una pista identificada como ambiente en nombre, ruta o etiquetas.
          Si no hay ninguna, omite [MUSICA] antes que sustituirla por una canción de meme o un tema no relacionado.
        - Efecto: [SFX] <referencia del catálogo> | esperar=no | volumen=45
        - Diálogo NUEVO: <nombre exacto de personaje>: <texto sin salto de línea>
          Solo en modo historia y si el personaje tiene perfil de voz. Nunca uses dos puntos para otra acción.
        - Pausa: [PAUSA] <entero de milisegundos>
          Ejemplo válido: [PAUSA] 500
          El campo termina después del número: NO [PAUSA] 500 y Bart: Hola.
        - Cámara: [CAMARA] <personaje en pantalla> | zoom=1.4 | duracion=300 | enfoque=cara
          También [CAMARA] habla | zoom=1.3 (sigue a quien habla) y [CAMARA] general | duracion=400 (plano completo).
          Úsala en pocos momentos (reacciones, sorpresas, remates) y vuelve a general después.
        - Barras de cine: [CINE] mostrar | estilo=cerrado | duracion=800 y luego [CINE] quitar | duracion=600
          (solo para momentos épicos o dramáticos; estilo abierto = barras finas).
        - Desenfoque: [DESENFOQUE] fondo | suavizar | duracion=600 (también ligero; quitar vuelve a enfocar). Objetivo:
          fondo, todos, personajes o un personaje.
        - Gesto del render: [GESTO] <personaje en pantalla> | balanceo (se inclina y rebota), rebote (se estira) o
          balanceo+rebote. [GESTO] habla | balanceo lo hace quien habla en cada línea, hasta [GESTO] habla | quitar.
        - Transición global: [TRANSICION] cruce | duracion=500 | vegas=flash | capas=fondo
          También entrada, salida o cambio; solo cruce admite vegas y capas.
          capas: fondo, personajes, imagenes, videos o todos; se pueden combinar con coma.

        OPCIONES COMPATIBLES (solo después de |, excepto diálogo, pausa y ocultar):
        Fondo/render/imagen/video: ancho=64..1280, alto=64..720, x=-1280..1280, y=-720..720,
        rotacion=-180..180, cambiar direccion=si/no, voltear h=si/no, voltear v=si/no, duracion=<ms>,
        transicion=heredar/corte/fundido/disolvente/flash/barrido.
        Para que un personaje mire hacia el otro lado usa cambiar direccion=si: se queda en su sitio. voltear h=si
        es un espejo de la capa: también la pasa al lado contrario del cuadro (izquierda ↔ derecha).
        Para mover una capa DURANTE su aparición: animar x=-1280..1280, animar y=-720..720,
        animar giro=-720..720 y animar ms=0..86400000. Son DELTAS desde x/y/rotacion iniciales;
        animar ms=0 usa toda la duración visible. Ejemplo: [MOSTRAR] Bart | A2 | derecha | animar x=-250 | animar giro=12 | animar ms=2400.
        Fondo: bordes=si/no. Video: capa=guion/fondo/sobre, croma=si/no,
        color=RRGGBB, tolerancia=0.01..1, volumen=0..200.
        Música/SFX: volumen=0..200, duracion=<ms>, modo audio=loop/tempo; SFX también esperar=si/no.
        Visual con efecto VEGAS de catálogo: transicion=plugin | vegas=<nombre o ID del plugin> | preset=<nombre exacto>.
        Transición cruce: vegas=flash/barrido/disolvente o plugin del catálogo, duracion=80..10000.
        Usa opciones escasas y válidas; NO inventes claves ni abreviaturas. La duración está en milisegundos.
        Si necesitas pausa tras una frase, primero el diálogo y después [PAUSA] <ms> como otra acción.
        No generes narración nueva ni texto en pantalla: el compositor no las sintetiza aquí.
        """;

    /// <summary>Base prompt for the typed schema (hotfix 18). The schema already enforces syntax,
    /// valid options and which references fit each action, so this only carries directing criteria.</summary>
    public const string TypedInstructions = """
        Eres el Director de Loquendo AI: conviertes el encargo en una escena corta, con ritmo y humor.
        Responde SOLO con el JSON del esquema: {"steps":[acción, acción, …]}. Cada acción es un objeto con "accion".
        El esquema ya limita qué recursos (A1, A2…) y personajes sirven en cada acción: elige entre ellos.

        CRITERIOS DE DIRECCIÓN:
        - Empieza con el fondo y, si encaja, música de fondo (volumen 10–30).
        - Muestra a cada personaje con "mostrar" antes de que hable; usa "ocultar" cuando salga de escena.
        - Varía los renders: cuando cambie la emoción o la intención de un personaje, muéstralo con OTRO render suyo
          cuyo nombre, carpeta o descripción encaje con ese momento. No repitas siempre el mismo.
        - Diálogos breves y naturales en español; una intervención por acción "dialogo". Sin narrador ni texto en pantalla.
        - Pausas de 300 a 1500 ms para dar ritmo a los chistes y reacciones.
        - SFX en el momento exacto de la acción (volumen 40–80, esperar=false salvo que el sonido deba terminar antes de seguir).
        - Si piden música ambiental, usa solo una pista identificada como ambiente; si no hay, omite la música.
        - Valores neutros cuando no hagan falta: transicion "heredar", encuadre "auto", animar_x/animar_y/animar_ms 0,
          duracion_ms 0. animar_x/animar_y desplazan la capa mientras aparece (px) durante animar_ms.
        - "transicion" como acción separa momentos o escenas; vegas y capas solo importan con estilo "cruce".
        - Las transiciones T1, T2… son efectos de VEGAS para 1 a 3 momentos clave (cambio de lugar, entrada sorpresa,
          remate), elegidas por su nombre: acción "transicion" con estilo "cruce" y vegas=Tn, seguida del nuevo fondo o
          personaje. La "transicion" de fondo/mostrar/imagen solo actúa justo después de un cruce; si no, deja "heredar".
        - CÁMARA ("camara"): acerca TODO el cuadro (fondo incluido) hacia un personaje que ya esté en pantalla.
          Úsala en 1 a 4 momentos por escena: reacción, sorpresa, remate de un chiste, amenaza o drama. No en cada frase.
          objetivo = el personaje (después de su "mostrar"), "quien habla" (sigue a cada hablante mientras dura) o
          "general" para volver al plano completo. zoom 120–160 normal, 170–250 solo para golpes dramáticos.
          duracion_ms 0 = corte seco (golpe), 200–600 = acercamiento rápido, 800–2000 = acercamiento lento y tenso.
          enfoque "cara" para reacciones, "cuerpo" si importa la pose. Tras el momento, vuelve con "general".
          En "general", zoom y enfoque se ignoran (pon 110 y "cara").
        - CINE ("cine"): barras negras de cine arriba y abajo. Solo si el encargo pide tono épico, dramático,
          de película, flashback o suspenso: "mostrar" al empezar ese momento (estilo "cerrado" = barras anchas,
          "abierto" = finas; duracion_ms 600–1200 para que entren despacio) y "quitar" al terminarlo.
          Como mucho una vez por escena; si toda la escena es "de película", ponlas al principio y déjalas.
        - DESENFOQUE ("desenfoque"): desenfoque gaussiano de VEGAS. "fondo" para centrar la atención en los personajes
          (conversación íntima, momento emotivo), un personaje para que parezca lejos o fuera de foco, "todos" para un
          recuerdo, sueño, mareo o para simular que la cámara se acerca y enfoca. "suavizar" desenfoca más, "ligero" menos;
          "quitar" vuelve a enfocar. duracion_ms 400–1500 para enfocar/desenfocar poco a poco, 0 de golpe. Úsalo poco
          (0 a 2 veces por escena) y quítalo cuando el momento termine.
        - GESTO ("gesto"): el render se inclina y rebota ("balanceo"), se estira desde los pies ("rebote") o ambos
          ("balanceo+rebote"), como en los videos Loquendo clásicos. Úsalo justo antes de una frase con energía
          (grito, sorpresa, chiste, enfado) de un personaje que ya esté en pantalla. personaje "quien habla" hace que
          cada hablante gesticule al empezar cada línea (vivo, para escenas de discusión o comedia rápida) hasta
          "quitar". No lo pongas en cada frase si ya usaste "quien habla".
        """;

    /// <summary>A saved master prompt written for the old line syntax ([FONDO] A1 | …).</summary>
    public static bool IsLegacyPrompt(string? prompt) => prompt is not null &&
        (prompt.Contains("GRAMÁTICA", StringComparison.OrdinalIgnoreCase) ||
         prompt.Contains("[FONDO] A1", StringComparison.OrdinalIgnoreCase) ||
         prompt.Contains("En line copia", StringComparison.OrdinalIgnoreCase));

    /// <summary>LOQUENDO_AI_DIRECTOR_LEGACY=1 restores the free-text line output (pre hotfix 18).</summary>
    public static bool UseLegacyOutput =>
        Environment.GetEnvironmentVariable("LOQUENDO_AI_DIRECTOR_LEGACY") is "1" or "true" or "si";

    public static string BaseInstructions => UseLegacyOutput ? Instructions : TypedInstructions;

    public static string ForTypedMode(bool recorded, string? customInstructions = null)
    {
        var custom = string.IsNullOrWhiteSpace(customInstructions) ? null : customInstructions.Trim();
        var text = custom ?? TypedInstructions;
        if (IsLegacyPrompt(custom))
            text += """


                FORMATO ACTUAL (prevalece sobre lo anterior): no escribas líneas con corchetes ni el campo line.
                Cada paso es un objeto de acción del esquema ("accion": fondo, mostrar, dialogo, sfx…).
                """;
        // Fixed rule (1.4.1), also with a custom master prompt: renders without a registered character.
        text += """


            RENDERS NPC: si «Renders por personaje» o el esquema traen «NPC» (extras sin personaje registrado), muéstralos
            con "mostrar" y personaje "NPC" y retíralos con "ocultar_npc" usando EL MISMO render. "ocultar" es solo para
            personajes. Los NPC no hablan con "dialogo" ni son objetivo de cámara, gesto o desenfoque.
            """;
        return text + (recorded
            ? """


              MODO VOCES GRABADAS: no escribas diálogos nuevos. Incluye CADA bloque original (B1, B2…) exactamente
              una vez con la acción "conservar", en el orden que mejor cuente la escena, e intercala entre ellos
              las acciones visuales y sonoras. No cambies el texto ni el audio de esos bloques.
              """
            : """


              MODO HISTORIA: escribe la escena completa con diálogos nuevos (acción "dialogo").
              """);
    }

    public static string ForMode(bool recorded, string? customInstructions = null) =>
        (string.IsNullOrWhiteSpace(customInstructions) ? Instructions : customInstructions.Trim()) + (recorded
        ? """

          MODO VOCES GRABADAS: devuelve {"steps":[{"line":"...","blockId":""},{"line":"","blockId":"ID ORIGINAL"}]}.
          Para una acción nueva, line contiene una sola instrucción y blockId es "".
          Para conservar un bloque existente, line es "" y blockId es exactamente un ID original de la lista.
          Incluye CADA blockId original exactamente una vez; no añadas diálogos ni edites WAV o transcripciones.
          """
        : """

          MODO HISTORIA: devuelve {"steps":[{"line":"..."}]}.
          No existen bloques originales en este modo. Cada objeto contiene SOLO line;
          no escribas blockId ni GUID de bloques. Escribe acciones y diálogos nuevos.
          """);
}

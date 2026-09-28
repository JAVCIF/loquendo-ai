using System.IO.Compression;
using System.Text;
using LoquendoAI.Infrastructure.Dialogue;
using static LoquendoAI.Infrastructure.Dialogue.DialogueScript;

namespace LoquendoAI.Tests;

/// <summary>Dialogue module (1.2.0): import formats, export round trip, scenes and AI answers.</summary>
internal static class DialogueTests
{
    private static string Row(DialogueEntry x) => $"{x.Kind}|{x.Speaker}|{x.Text}|{x.Comment}";

    [Test("Diálogos: formato recomendado (Nombre: texto, narración, [ESCENA], acotaciones, notas)")]
    public static void Recommended()
    {
        var rows = ParseText("""
            # nota del autor, se ignora
            [ESCENA] En la cocina
            Bart: ¡Hola amigos!
            Homero (enojado): ¡Bart!
            Lisa: (susurrando) No grites.
            Esta línea no tiene nombre y es narración.
            Esto es una frase larga con dos puntos: sigue siendo narración
            Narrador: Mientras tanto…
            (entra Marge)
            Marge:
            ¿Qué pasa aquí?
            **Sr. Burns:** Excelente.
            - Milhouse: ¿Puedo jugar?
            Bart dice: ¡Ay caramba!
            Homero gritó: ¡Marge!
            Y entonces dijo: nada más.
            Chica del bar: ¿Qué te sirvo?
            """, DialogueParseMode.Recommended);
        Assert.Sequence(new[]
        {
            "SceneBreak||En la cocina|",
            "Dialogue|Bart|¡Hola amigos!|",
            "Dialogue|Homero|¡Bart!|enojado",
            "Dialogue|Lisa|No grites.|susurrando",
            "Narration|Narrador|Esta línea no tiene nombre y es narración.|",
            "Narration|Narrador|Esto es una frase larga con dos puntos: sigue siendo narración|",
            "Narration|Narrador|Mientras tanto…|",
            "Dialogue|Marge|¿Qué pasa aquí?|entra Marge",
            "Dialogue|Sr. Burns|Excelente.|",
            "Dialogue|Milhouse|¿Puedo jugar?|",
            "Dialogue|Bart|¡Ay caramba!|",
            "Dialogue|Homero|¡Marge!|",
            "Narration|Narrador|Y entonces dijo: nada más.|",
            "Dialogue|Chica del bar|¿Qué te sirvo?|"
        }, rows.Select(Row), "filas");
    }

    [Test("Diálogos: modo análisis (guion de cine, novela, chat, tiempos, encabezados)")]
    public static void Analysis()
    {
        var screenplay = ParseText("""
            INT. CASA DE LOS SIMPSON - NOCHE

            Homero entra por la puerta.

            HOMERO
            ¡Marge, ya llegué!

            MARGE (O.S.)
            (desde la cocina)
            Estoy aquí.

            BART
            Ay, caramba.

            CORTE A:
            """, DialogueParseMode.Analysis);
        Assert.Sequence(new[]
        {
            "SceneBreak||Casa De Los Simpson - Noche|",
            "Narration|Narrador|Homero entra por la puerta.|",
            "Dialogue|Homero|¡Marge, ya llegué!|",
            "Dialogue|Marge|Estoy aquí.|desde la cocina",
            "Dialogue|Bart|Ay, caramba.|"
        }, screenplay.Select(Row), "guion de cine");
        Assert.True(screenplay[1].Warning is not null, "la descripción queda marcada para revisar");

        var novel = ParseText("""
            ## Escena 2: El parque
            —Hola —dijo Bart—. ¿Vienes?
            —¿Adónde? —preguntó Lisa.
            «Nunca», respondió Homero.
            —Nadie sabe quién dijo esto.
            [Milhouse] ¿Y yo?
            00:01:05 Nelson: ¡Ja, ja!
            Skinner - Silencio.
            Homero suspiró — otra vez.
            3 días después volvieron.
            """, DialogueParseMode.Analysis);
        Assert.Sequence(new[]
        {
            "SceneBreak||El parque|",
            "Dialogue|Bart|Hola ¿Vienes?|",
            "Dialogue|Lisa|¿Adónde?|",
            "Dialogue|Homero|Nunca|",
            "Dialogue||Nadie sabe quién dijo esto.|",
            "Dialogue|Milhouse|¿Y yo?|",
            "Dialogue|Nelson|¡Ja, ja!|",
            "Dialogue|Skinner|Silencio.|",
            "Narration|Narrador|Homero suspiró — otra vez.|",
            "Narration|Narrador|3 días después volvieron.|"
        }, novel.Select(Row), "novela, chat y otros");
        Assert.True(novel[4].Warning?.Contains("no identificado") == true, "sin atribución: se avisa");
    }

    [Test("Diálogos: tablas con y sin encabezado, CSV, Excel y Word")]
    public static void Tables()
    {
        var withHeader = ParseTable(new[]
        {
            new[] { "Tipo", "Personaje", "Comentario", "Diálogo" },
            new[] { "Escena", "", "", "Casa" },
            new[] { "", "Bart", "gritando", "¡Ay caramba!" },
            new[] { "Narración", "", "", "Y así empezó todo." },
            new[] { "", "Lisa", "", "(suspira) Otra vez no." }
        }, DialogueParseMode.Recommended);
        Assert.Sequence(new[]
        {
            "SceneBreak||Casa|", "Dialogue|Bart|¡Ay caramba!|gritando", "Narration|Narrador|Y así empezó todo.|",
            "Dialogue|Lisa|Otra vez no.|suspira"
        }, withHeader.Select(Row), "con encabezados");

        var guessed = ParseTable(new[]
        {
            new[] { "1", "Bart", "Hola, ¿cómo están todos hoy?" },
            new[] { "2", "Lisa", "Muy bien, gracias por preguntar." },
            new[] { "3", "Bart", "Me alegro mucho de oírlo." }
        }, DialogueParseMode.Analysis);
        Assert.Sequence(new[] { "Bart", "Lisa", "Bart" }, guessed.Select(x => x.Speaker), "sin encabezados: columna de nombres");

        var csv = ReadCsv("personaje;texto\r\nBart;\"Hola; \"\"amigos\"\"\"\r\nLisa;Adiós\r\n");
        Assert.Sequence(new[] { "Dialogue|Bart|Hola; \"amigos\"|", "Dialogue|Lisa|Adiós|" },
            ParseTable(csv, DialogueParseMode.Recommended).Select(Row), "CSV con comillas y punto y coma");

        var xlsx = Zip(new Dictionary<string, string>
        {
            ["xl/workbook.xml"] = """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Guion" sheetId="1" r:id="rId1"/></sheets></workbook>""",
            ["xl/_rels/workbook.xml.rels"] = """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="x" Target="worksheets/hoja.xml"/></Relationships>""",
            ["xl/sharedStrings.xml"] = """<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>Quién habla</t></si><si><t>Texto</t></si><si><r><t>Ho</t></r><r><t>mero</t></r></si><si><t>¡Mmm, rosquillas!</t></si></sst>""",
            ["xl/worksheets/hoja.xml"] = """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="B1" t="s"><v>0</v></c><c r="C1" t="s"><v>1</v></c></row><row r="2"><c r="B2" t="s"><v>2</v></c><c r="C2" t="s"><v>3</v></c></row><row r="3"><c r="B3" t="inlineStr"><is><t>Marge</t></is></c><c r="C3" t="inlineStr"><is><t>Homero…</t></is></c></row></sheetData></worksheet>"""
        });
        Assert.Sequence(new[] { "Dialogue|Homero|¡Mmm, rosquillas!|", "Dialogue|Marge|Homero…|" },
            ParseTable(ReadXlsx(xlsx), DialogueParseMode.Recommended).Select(Row), "Excel: hoja del libro, textos compartidos y en línea");

        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var docx = Zip(new Dictionary<string, string>
        {
            ["word/document.xml"] = $"""<w:document xmlns:w="{w}"><w:body><w:p><w:r><w:t>[ESCENA] Inicio</w:t></w:r></w:p><w:p><w:r><w:t xml:space="preserve">Bart: </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>Hola</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Personaje</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Diálogo</w:t></w:r></w:p></w:tc></w:tr><w:tr><w:tc><w:p><w:r><w:t>Lisa</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Hola, Bart.</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p><w:r><w:t>Fin del episodio.</w:t></w:r></w:p></w:body></w:document>"""
        });
        Assert.Sequence(new[]
        {
            "SceneBreak||Inicio|", "Dialogue|Bart|Hola|", "Dialogue|Lisa|Hola, Bart.|", "Narration|Narrador|Fin del episodio.|"
        }, ParseDocx(docx, DialogueParseMode.Recommended).Select(Row), "Word: párrafos y tablas en orden");

        Assert.Equal("Canción", ReadText(Encoding.Latin1.GetBytes("Canción")), "texto ANSI");
        Assert.Equal("—Hola —dijo Bart…", ReadText([0x97, .. Encoding.Latin1.GetBytes("Hola "), 0x97, .. Encoding.Latin1.GetBytes("dijo Bart"), 0x85]),
            "ANSI de Windows (1252): rayas y puntos suspensivos");
        Assert.Sequence(new[] { "Dialogue|Bart|Hola|", "Dialogue|Lisa|Adiós|" },
            ParseTable(new[] { new[] { "Bart: Hola\nLisa: Adiós" } }, DialogueParseMode.Recommended).Select(Row), "celda con varios párrafos");
        Assert.Equal("Canción", ReadText([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Canción")]), "UTF-8 con BOM");
    }

    [Test("Diálogos: exportar a texto y volver a importar da las mismas filas")]
    public static void RoundTrip()
    {
        var rows = new[]
        {
            new DialogueEntry(DialogueKind.SceneBreak, "", "La cocina"),
            new DialogueEntry(DialogueKind.Narration, Narrator, "Nota: esto es narración con dos puntos."),
            new DialogueEntry(DialogueKind.Dialogue, "Bart", "¡Hola!", "gritando (mucho)"),
            new DialogueEntry(DialogueKind.Dialogue, "Sr. Burns", "Excelente.")
        };
        var text = ToText(rows);
        Assert.Sequence(new[]
        {
            "SceneBreak||La cocina|", "Narration|Narrador|Nota: esto es narración con dos puntos.|",
            "Dialogue|Bart|¡Hola!|gritando [mucho]", "Dialogue|Sr. Burns|Excelente.|"
        }, ParseText(text, DialogueParseMode.Recommended).Select(Row), "ida y vuelta");
    }

    [Test("Diálogos: reparto en escenas por cortes y por el máximo de líneas")]
    public static void Scenes()
    {
        var rows = new List<DialogueEntry> { new(DialogueKind.Dialogue, "Bart", "antes del primer corte") };
        rows.Add(new DialogueEntry(DialogueKind.SceneBreak, "", "Escuela", "lugar: aula"));
        rows.AddRange(Enumerable.Range(1, 50).Select(i => new DialogueEntry(DialogueKind.Dialogue, "Lisa", $"línea {i}")));
        rows.Add(new DialogueEntry(DialogueKind.SceneBreak, "", "Vacía"));
        rows.Add(new DialogueEntry(DialogueKind.SceneBreak, "", "Casa"));
        rows.Add(new DialogueEntry(DialogueKind.Narration, Narrator, "fin"));
        var parts = SplitScenes(rows, 40);
        Assert.Sequence(new[] { "Parte 1", "Escuela (1/2)", "Escuela (2/2)", "Casa" }, parts.Select(x => x.Title), "títulos");
        Assert.Sequence(new[] { 1, 25, 25, 1 }, parts.Select(x => x.Lines.Count), "líneas por escena");
        Assert.Equal("lugar: aula", parts[1].Notes, "notas del corte");
        Assert.Equal(rows.Count(x => x.Kind != DialogueKind.SceneBreak), parts.Sum(x => x.Lines.Count), "no se pierde ninguna línea");
        Assert.True(parts.All(x => x.Lines.Count <= MaxLinesPerScene), "límite por escena");
        Assert.Equal(MaxLines, EpisodeLimit, "480 = 12 escenas × 40 líneas del Director por episodio");
    }

    private static int EpisodeLimit =>
        LoquendoAI.Infrastructure.Director.EpisodePlanning.EpisodeMaxScenes * LoquendoAI.Infrastructure.Director.EpisodePlanning.EpisodeMaxTakesPerScene;

    [Test("Diálogos: respuesta de la IA (JSON del esquema o texto) y prompt para IA web")]
    public static void Ai()
    {
        var rows = ParseAiOutput("""
            ```json
            {"lineas":[{"tipo":"escena","personaje":"","texto":"El bar de Moe","acotacion":""},
             {"tipo":"narracion","personaje":"Narrador","texto":"  Viernes   por la noche. ","acotacion":""},
             {"tipo":"dialogo","personaje":"MOE","texto":"¿Qué van a tomar?","acotacion":"(de mal humor)"},
             {"tipo":"dialogo","personaje":"Barney","texto":"","acotacion":""}]}
            ```
            """);
        Assert.Sequence(new[]
        {
            "SceneBreak||El bar de Moe|", "Narration|Narrador|Viernes por la noche.|", "Dialogue|MOE|¿Qué van a tomar?|de mal humor"
        }, rows.Select(Row), "JSON: filas vacías fuera, espacios limpios");
        Assert.Sequence(new[] { "Dialogue|Bart|Hola|" }, ParseAiOutput("Bart: Hola").Select(Row), "texto en vez de JSON");

        var prompt = WebPrompt("", CharactersSection([("Bart", "niño travieso"), ("Lisa", "")], ["Vendedor"]), "", 30);
        Assert.True(prompt.Contains("- Bart: niño travieso") && prompt.Contains("- Lisa\n") && prompt.Contains("- Vendedor (secundario)"),
            "personajes elegidos y secundarios");
        Assert.True(prompt.Contains("[ESCENA]") && prompt.Contains("unas 30 líneas") && prompt.Contains("[Escribe aquí"),
            "formato, extensión y hueco para la historia");
        var imported = ParseText(TextFormatRules[TextFormatRules.IndexOf("[ESCENA] En la cocina", StringComparison.Ordinal)..], DialogueParseMode.Recommended);
        Assert.Sequence(new[] { "SceneBreak", "Narration", "Dialogue", "Dialogue" }, imported.Select(x => x.Kind.ToString()),
            "el ejemplo del prompt se importa tal cual");
    }

    private static byte[] Zip(Dictionary<string, string> files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        return memory.ToArray();
    }
}

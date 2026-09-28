using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LoquendoAI.Infrastructure.Dialogue;

/// <summary>What a row of the dialogue module is: a line with a speaker, a narration, or a scene cut
/// (its text is the title of the scene that starts there).</summary>
public enum DialogueKind { Dialogue, Narration, SceneBreak }

/// <summary>How imported text is read. Recommended: «Nombre: texto», a line without name is narration,
/// «[ESCENA] título» cuts. Analysis also understands screenplays, novel dialogue («—Hola —dijo Bart»),
/// chat logs, numbered lines, «Nombre - texto» and headings.</summary>
public enum DialogueParseMode { Recommended, Analysis }

/// <summary>A parsed row. <see cref="Warning"/> explains what the parser had to guess.</summary>
public sealed record DialogueEntry(DialogueKind Kind, string Speaker, string Text, string Comment = "", string? Warning = null);

/// <summary>A group of consecutive rows that becomes one scene: its title, notes and the indexes of its lines.</summary>
public sealed record DialogueScenePart(string Title, string Notes, IReadOnlyList<int> Lines);

/// <summary>
/// The dialogue module without interface (1.2.0): import from plain text, Word, Excel and CSV, the recommended
/// text format (also used to export and for the web AI), splitting into scenes and the AI prompt and schema.
/// </summary>
public static class DialogueScript
{
    /// <summary>Rows of a dialogue script: 12 scenes × 40 lines, what the episode Director accepts.</summary>
    public const int MaxLines = 480;
    public const int MaxLinesPerScene = 40;
    public const int MaxTextLength = 1000;
    /// <summary>Lines one in-app AI request asks for; more are generated in several runs («Añadir»).</summary>
    public const int MaxAiLinesPerRun = 150;
    public const string Narrator = "Narrador";

    // ───────────────────────────── Text ─────────────────────────────

    private static readonly Regex SceneTag = new(@"^\[\s*(ESCENA|ESCENE|SCENE)\s*\]\s*[:\-–—]?\s*(?<title>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SpeakerLine = new(@"^(?<name>[^:：]{1,60}?)\s*[:：]\s*(?<text>.*)$");
    private static readonly Regex ListMarker = new(@"^\s*(?:[-*•·]\s+|\d{1,4}[.)]\s+)");
    /// <summary>Subtitle-like timestamps («00:12», «[01:02:03]»); numbered lines are list markers.</summary>
    private static readonly Regex Numbering = new(@"^\s*\[?\d{1,2}:\d{2}(?::\d{2})?(?:[.,]\d{1,3})?\]?\s+(?=\S)");
    private static readonly Regex ChatLine = new(@"^(?:\[(?<name>[^\]]{1,40})\]|<(?<name>[^>]{1,40})>)\s*[:：]?\s*(?<text>.+)$");
    private static readonly Regex DashLine = new(@"^(?<name>[\p{Lu}][\p{L}\p{M}'.\s]{0,39}?)\s+[-–—>]{1,2}\s+(?<text>.+)$");
    private static readonly Regex Parenthetical = new(@"^\((?<text>[^()]*)\)$");
    private static readonly Regex LeadingParenthetical = new(@"^\((?<comment>[^()]{1,200})\)\s*(?<text>.*)$");
    private static readonly Regex NameParenthetical = new(@"^(?<name>[^()]+?)\s*\((?<comment>[^()]{1,120})\)\s*$");
    private static readonly Regex Slugline = new(@"^(?:INT|EXT|INT\.?\s*/\s*EXT|I/E)\.?\s+(?<title>.+)$", RegexOptions.IgnoreCase);
    private static readonly Regex SceneHeading = new(@"^(?:#{1,6}\s*)?(?:ESCENA|SCENE|CAP[IÍ]TULO|PARTE)\s*(?:\d+|(?-i:[IVXLC]+)\b)?\s*[:.\-–—]?\s*(?<title>.*)$",
        RegexOptions.IgnoreCase);
    private static readonly Regex ScreenplayTransition = new(@"^(?:CORTE|FUNDIDO|DISOLVENCIA|CUT TO|FADE|SMASH CUT|DISSOLVE)\b.*$");
    private static readonly Regex ScreenplayExtension = new(@"\s*\((?:V\.?O\.?|O\.?S\.?|O\.?C\.?|CONT'?D|CONT\.|OFF|EN OFF|VOZ EN OFF)\)\s*$",
        RegexOptions.IgnoreCase);
    private static readonly Regex Attribution = new(
        @"(?:\b(?:dij[oe]|pregunt[oó]|respondi[oó]|contest[oó]|grit[oó]|susurr[oó]|exclam[oó]|murmur[oó]|a[ñn]adi[oó]|explic[oó]|" +
        @"replic[oó]|insisti[oó]|coment[oó]|pens[oó]|llam[oó]|suspir[oó]|ri[oó]|interrumpi[oó]|protest[oó]|anunci[oó]|balbuce[oó])\s+" +
        @"(?:el\s+|la\s+)?(?<name>\p{Lu}[\p{L}\p{M}'-]+(?:\s+\p{Lu}[\p{L}\p{M}'-]+)?))|" +
        @"(?:^\s*(?<name2>\p{Lu}[\p{L}\p{M}'-]+(?:\s+\p{Lu}[\p{L}\p{M}'-]+)?)\s+(?:dij[oe]|pregunt[oó]|respondi[oó]|contest[oó]|grit[oó]|susurr[oó]|exclam[oó])\b)");

    /// <summary>Reads plain text (one row per line; see <see cref="DialogueParseMode"/>).</summary>
    public static IReadOnlyList<DialogueEntry> ParseText(string text, DialogueParseMode mode) =>
        ParseLines((text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'), mode);

    public static IReadOnlyList<DialogueEntry> ParseLines(IEnumerable<string> rawLines, DialogueParseMode mode)
    {
        var lines = rawLines.Select(x => (x ?? "").Replace(' ', ' ').TrimEnd()).ToArray();
        var result = new List<DialogueEntry>();
        var pendingComment = "";
        string? pendingSpeaker = null;         // «Bart:» alone on its line: the next line is his
        string? screenplaySpeaker = null;      // screenplay cue: every line until a blank one
        var screenplay = mode == DialogueParseMode.Analysis && LooksLikeScreenplay(lines);

        void Add(DialogueKind kind, string speaker, string text, string? warning = null)
        {
            text = CleanText(text);
            if (text.Length == 0 && kind != DialogueKind.SceneBreak) return;
            if (text.Length > MaxTextLength)
                warning = Join(warning, $"Texto de {text.Length} caracteres: el TTS puede tardar o fallar; conviene partirlo");
            result.Add(new DialogueEntry(kind, speaker, text, pendingComment, warning));
            pendingComment = "";
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                screenplaySpeaker = null;
                continue;
            }
            if (mode == DialogueParseMode.Recommended && (line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)))
                continue; // notes of the writer
            if (SceneTag.Match(line) is { Success: true } tag)
            {
                pendingSpeaker = screenplaySpeaker = null;
                Add(DialogueKind.SceneBreak, "", tag.Groups["title"].Value);
                continue;
            }
            if (mode == DialogueParseMode.Analysis)
            {
                if (line.StartsWith('#') && SceneHeading.Match(StripMarkdown(line)) is not { Success: true })
                    continue; // other headings: titles of the document
                if (Slugline.Match(line) is { Success: true } slug)
                {
                    pendingSpeaker = screenplaySpeaker = null;
                    Add(DialogueKind.SceneBreak, "", Capitalize(slug.Groups["title"].Value));
                    continue;
                }
                if (SceneHeading.Match(StripMarkdown(line)) is { Success: true } heading && IsHeading(line))
                {
                    pendingSpeaker = screenplaySpeaker = null;
                    Add(DialogueKind.SceneBreak, "", heading.Groups["title"].Value);
                    continue;
                }
                if (screenplay && ScreenplayTransition.IsMatch(line) && line == line.ToUpperInvariant()) continue;
                line = Numbering.Replace(line, "", 1);
            }
            var unlisted = line; // «- Hola -dijo Bart» is novel dialogue, «- Bart: Hola» a list item
            line = ListMarker.Replace(line, "", 1).Trim();
            if (line.Length == 0) continue;

            if (Parenthetical.Match(line) is { Success: true } note)
            {
                pendingComment = Join(pendingComment, note.Groups["text"].Value.Trim(), "; ");
                continue;
            }

            if (screenplay && IsScreenplayCue(line))
            {
                screenplaySpeaker = TitleCaseIfShouting(ScreenplayExtension.Replace(line, "").Trim());
                continue;
            }
            if (screenplaySpeaker is not null)
            {
                AddSpoken(screenplaySpeaker, line, null);
                continue;
            }

            if (TrySpeaker(line, mode, out var speaker, out var spoken, out var nameComment))
            {
                pendingSpeaker = null;
                if (nameComment.Length > 0) pendingComment = Join(pendingComment, nameComment, "; ");
                if (spoken.Trim().Length == 0)
                {
                    pendingSpeaker = speaker; // «Bart:» and the text on the next line
                    continue;
                }
                AddSpoken(speaker, spoken, null);
                continue;
            }
            if (pendingSpeaker is not null)
            {
                AddSpoken(pendingSpeaker, line, null);
                pendingSpeaker = null;
                continue;
            }
            if (mode == DialogueParseMode.Analysis && TryNovelDialogue(unlisted, out var novelSpeaker, out var novelText))
            {
                AddSpoken(novelSpeaker ?? "", novelText, novelSpeaker is null ? "Hablante no identificado: elige quién habla" : null);
                continue;
            }
            Add(DialogueKind.Narration, Narrator, line,
                screenplay ? "Descripción del guion: bórrala si no debe narrarse" : null);
        }
        return result;

        void AddSpoken(string speaker, string text, string? warning)
        {
            text = text.Trim();
            if (LeadingParenthetical.Match(text) is { Success: true } lead)
            {
                pendingComment = Join(pendingComment, lead.Groups["comment"].Value.Trim(), "; ");
                text = lead.Groups["text"].Value;
            }
            if (IsNarrator(speaker)) Add(DialogueKind.Narration, Narrator, text, warning);
            else Add(DialogueKind.Dialogue, speaker, text, warning);
        }
    }

    /// <summary>«Nombre: texto» (also «**Nombre:**», «Nombre (enojado): …»; in analysis «[Nombre] texto»,
    /// «&lt;Nombre&gt; texto» and «Nombre - texto»). A long prefix or one with sentence punctuation is not a name,
    /// so «Esto es una frase: sigue» stays narration.</summary>
    public static bool TrySpeaker(string line, DialogueParseMode mode, out string speaker, out string text, out string comment)
    {
        speaker = text = comment = "";
        var clean = StripMarkdownEmphasis(line);
        if (SpeakerLine.Match(clean) is { Success: true } match && ValidName(WithoutSayVerb(match.Groups["name"].Value), out speaker, out comment))
        {
            text = match.Groups["text"].Value;
            return !LooksLikeClock(match.Groups["name"].Value, text);
        }
        if (mode != DialogueParseMode.Analysis) return false;
        if (ChatLine.Match(clean) is { Success: true } chat && ValidName(chat.Groups["name"].Value, out speaker, out comment))
        {
            text = chat.Groups["text"].Value;
            return true;
        }
        if (DashLine.Match(clean) is { Success: true } dash && ValidName(dash.Groups["name"].Value, out speaker, out comment) &&
            speaker.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3)
        {
            text = dash.Groups["text"].Value;
            return true;
        }
        return false;
    }

    private static readonly HashSet<string> SpeechVerbs = new(StringComparer.Ordinal)
    {
        "DIJO", "DICE", "DIJE", "GRITA", "GRITO", "PREGUNTA", "PREGUNTO", "RESPONDE", "RESPONDIO", "CONTESTA", "CONTESTO",
        "SUSURRA", "SUSURRO", "EXCLAMA", "EXCLAMO", "MURMURA", "MURMURO", "ANADE", "ANADIO", "EXPLICA", "EXPLICO", "PIENSA",
        "PENSO", "SUSPIRA", "SUSPIRO", "REPLICA", "REPLICO", "COMENTA", "COMENTO", "ANUNCIA", "ANUNCIO", "ENTONCES", "LUEGO",
        "DESPUES", "ES", "ERA", "FUE", "ESTA", "ESTABA", "HAY", "HABIA", "DECIA", "SAID", "SAYS", "ASKED"
    };

    private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase)
        { "de", "del", "la", "las", "el", "los", "y", "van", "von", "da", "di", "du", "le", "mc", "of", "the" };

    /// <summary>«Bart dice: hola» / «Homero gritó: ¡Marge!» → the name before the verb of saying.</summary>
    private static string WithoutSayVerb(string name)
    {
        var words = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 && SayVerbs.Contains(Fold(words[^1])) ? string.Join(' ', words[..^1]) : name;
    }

    private static readonly HashSet<string> SayVerbs = new(StringComparer.Ordinal)
    {
        "DIJO", "DICE", "GRITA", "GRITO", "PREGUNTA", "PREGUNTO", "RESPONDE", "RESPONDIO", "CONTESTA", "CONTESTO", "SUSURRA",
        "SUSURRO", "EXCLAMA", "EXCLAMO", "MURMURA", "MURMURO", "REPLICA", "REPLICO", "ANUNCIA", "ANUNCIO"
    };

    private static bool LooksLikeClock(string name, string text) =>
        name.Trim().All(char.IsDigit) && text.Length > 0 && char.IsDigit(text[0]);

    private static bool ValidName(string raw, out string name, out string comment)
    {
        comment = "";
        name = StripMarkdownEmphasis(raw).Trim().Trim('"', '\'', '«', '»', '“', '”');
        if (NameParenthetical.Match(name) is { Success: true } paren)
        {
            name = paren.Groups["name"].Value.Trim();
            comment = paren.Groups["comment"].Value.Trim();
        }
        name = ScreenplayExtension.Replace(name, "").Trim();
        if (name.Length is 0 or > 40 || !char.IsLetter(name[0])) return false;
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 4) return false;
        // A name, not the start of a sentence: no speech verbs («Homero gritó:», «Y entonces dijo:») and, from the
        // third word on, a lowercase word only after a connector («El Chavo del Ocho», «Chica del bar»; not «Esto es una frase:»).
        if (words.Any(w => SpeechVerbs.Contains(Fold(w.Trim(',', '.'))))) return false;
        if (words.Length >= 3 && words.Skip(1).Where((w, i) => char.IsLower(w[0]) && !Connectors.Contains(w) && !Connectors.Contains(words[i])).Any())
            return false;
        var withoutAbbreviations = Regex.Replace(name, @"\b\p{L}{1,4}\.(?=\s|$)", "X"); // «Sr. Burns», «Dr. Nick»
        if (withoutAbbreviations.IndexOfAny(['.', ',', ';', '!', '?', '¿', '¡', '"', '«', '»', '“', '”', '(', ')', '[', ']', '<', '>', '/', '\\', '|', '=']) >= 0)
            return false;
        if (name.Count(char.IsDigit) > 2) return false;
        if (Uri.IsWellFormedUriString(name, UriKind.Absolute) || name.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        name = TitleCaseIfShouting(name);
        return true;
    }

    /// <summary>Spanish novel dialogue: «—Hola —dijo Bart—. ¿Vienes?» → Bart: «Hola ¿Vienes?»; also «Hola», dijo Bart.</summary>
    public static bool TryNovelDialogue(string line, out string? speaker, out string text)
    {
        speaker = null;
        text = "";
        var trimmed = line.TrimStart();
        if (trimmed.Length > 1 && trimmed[0] is '—' or '–' || trimmed.StartsWith("- ", StringComparison.Ordinal) ||
            trimmed.StartsWith("-", StringComparison.Ordinal) && trimmed.Length > 1 && char.IsLetter(trimmed[1]) && char.IsUpper(trimmed[1]) ||
            trimmed.StartsWith("-¿", StringComparison.Ordinal) || trimmed.StartsWith("-¡", StringComparison.Ordinal))
        {
            var body = trimmed.TrimStart('—', '–', '-', ' ');
            var parts = Regex.Split(body, @"\s*[—–]\s*|\s+-\s+");
            var spoken = new List<string>();
            for (var i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0) spoken.Add(parts[i]);
                else if (speaker is null && Attribution.Match(parts[i]) is { Success: true } attribution)
                    speaker = AttributionName(attribution);
            }
            text = string.Join(" ", spoken.Select(x => x.Trim().TrimStart('.', ',', ' ').Trim()).Where(x => x.Length > 0));
            return text.Length > 0;
        }
        var quoted = Regex.Matches(trimmed, "«(?<q>[^»]+)»|“(?<q>[^”]+)”|\"(?<q>[^\"]+)\"");
        if (quoted.Count > 0)
        {
            var outside = Regex.Replace(trimmed, "«[^»]+»|“[^”]+”|\"[^\"]+\"", " | ");
            if (Attribution.Match(outside) is { Success: true } attribution)
            {
                speaker = AttributionName(attribution);
                text = string.Join(" ", quoted.Select(x => x.Groups["q"].Value.Trim()));
                return text.Length > 0;
            }
        }
        return false;
    }

    private static string AttributionName(Match match) =>
        TitleCaseIfShouting((match.Groups["name"].Success ? match.Groups["name"].Value : match.Groups["name2"].Value).Trim());

    /// <summary>At least three short all-caps lines followed by text: a screenplay (cues on their own line).</summary>
    private static bool LooksLikeScreenplay(IReadOnlyList<string> lines)
    {
        var cues = 0;
        for (var i = 0; i + 1 < lines.Count; i++)
            if (IsScreenplayCue(lines[i].Trim()) && lines[i + 1].Trim().Length > 0) cues++;
        return cues >= 3;
    }

    private static bool IsScreenplayCue(string line)
    {
        var name = ScreenplayExtension.Replace(line, "").Trim();
        if (name.Length is < 2 or > 30 || name.EndsWith(':') || !name.Any(char.IsLetter)) return false;
        if (name != name.ToUpperInvariant()) return false;
        if (name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 3) return false;
        if (Slugline.IsMatch(name) || ScreenplayTransition.IsMatch(name) || SceneHeading.IsMatch(name) && name.Any(char.IsDigit)) return false;
        return name.All(c => char.IsLetter(c) || c is ' ' or '\'' or '-' or '.');
    }

    private static bool IsHeading(string line) =>
        line.StartsWith('#') || line.Length <= 60 && !line.TrimEnd().EndsWith('.') &&
        Regex.IsMatch(line, @"^(?:#{1,6}\s*)?(?:ESCENA|SCENE|CAP[IÍ]TULO|PARTE)\s*(?:\d+|(?-i:[IVXLC]+)\b)?\s*(?:[:.\-–—]|$)", RegexOptions.IgnoreCase);

    private static bool IsNarrator(string speaker) =>
        speaker.Length == 0 ? false :
        Regex.IsMatch(Fold(speaker), "^(NARRADOR|NARRADORA|NARRACION|NARRATOR|VOZ EN OFF|VOZ)$");

    /// <summary>Upper-case, without accents: how names are compared.</summary>
    public static string Fold(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var text = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) text.Append(char.ToUpperInvariant(c));
        return Regex.Replace(text.ToString(), @"\s+", " ");
    }

    private static string TitleCaseIfShouting(string name) =>
        name.Length > 3 && name == name.ToUpperInvariant() && name.Any(char.IsLetter)
            ? CultureInfo.GetCultureInfo("es-ES").TextInfo.ToTitleCase(name.ToLowerInvariant()) : name;

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : value == value.ToUpperInvariant() ? TitleCaseIfShouting(value) : value;

    private static string StripMarkdown(string line) => StripMarkdownEmphasis(line.TrimStart('#', ' '));

    private static string StripMarkdownEmphasis(string value) =>
        Regex.Replace(value, @"(\*\*|__|\*|_)(?=\S)(.+?)(?<=\S)\1", "$2").Replace("**", "");

    private static string CleanText(string text)
    {
        text = Regex.Replace(text.Trim(), @"\s+", " ");
        if (text.Length >= 2 && (text[0], text[^1]) is ('"', '"') or ('«', '»') or ('“', '”'))
            text = text[1..^1].Trim();
        return text;
    }

    private static string Join(string? a, string? b, string separator = " · ") =>
        string.IsNullOrWhiteSpace(a) ? b ?? "" : string.IsNullOrWhiteSpace(b) ? a : a + separator + b;

    // ───────────────────────────── Tables ─────────────────────────────

    /// <summary>
    /// A table (Excel, CSV, Word table). With a header row the columns are found by name (tipo, personaje/quién/
    /// speaker, diálogo/texto, comentario/acotación); without one, 1 column is text lines, 2 are speaker + text and
    /// with more the speaker is the short repeated column and the text the longest one.
    /// </summary>
    public static IReadOnlyList<DialogueEntry> ParseTable(IReadOnlyList<IReadOnlyList<string>> rows, DialogueParseMode mode)
    {
        var table = rows.Select(r => r.Select(c => (c ?? "").Trim()).ToArray())
            .Where(r => r.Any(c => c.Length > 0)).ToList();
        if (table.Count == 0) return [];
        var width = table.Max(r => r.Length);
        string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index] : "";

        int kind = -1, speaker = -1, text = -1, comment = -1;
        var header = table[0].Select(Fold).ToArray();
        for (var i = 0; i < header.Length; i++)
        {
            var h = header[i];
            if (kind < 0 && Regex.IsMatch(h, "^(TIPO|TYPE|CLASE)$")) kind = i;
            else if (speaker < 0 && Regex.IsMatch(h, "^(PERSONAJE|PERSONAJES|QUIEN( HABLA)?|HABLANTE|SPEAKER|CHARACTER|NOMBRE|NAME|ROL|ROLE|VOZ DE)$")) speaker = i;
            else if (text < 0 && Regex.IsMatch(h, "^(DIALOGO|DIALOGOS|TEXTO|TEXT|LINEA|LINE|FRASE|GUION|DIALOGUE|DIALOG|PARLAMENTO)$")) text = i;
            else if (comment < 0 && Regex.IsMatch(h, "^(COMENTARIO|COMENTARIOS|ACOTACION|ACOTACIONES|NOTA|NOTAS|COMMENT|COMMENTS|NOTES|DIRECCION)$")) comment = i;
        }
        var hasHeader = text >= 0;
        if (hasHeader) table.RemoveAt(0);
        else if (width == 1)
            return ParseLines(table.SelectMany(r => Cell(r, 0).Split('\n')), mode);
        else if (width == 2) { speaker = 0; text = 1; }
        else
        {
            // Text: the longest column on average; speaker: a short column with repeated values.
            var averages = Enumerable.Range(0, width).Select(c => table.Average(r => Cell(r, c).Length)).ToArray();
            text = Array.IndexOf(averages, averages.Max());
            var best = double.MaxValue;
            for (var c = 0; c < width; c++)
            {
                if (c == text) continue;
                var values = table.Select(r => Cell(r, c)).Where(v => v.Length > 0).ToArray();
                if (values.Length == 0 || averages[c] > 40) continue;
                var repeat = values.Length / (double)values.Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var names = values.Count(v => ValidName(v, out _, out _)) / (double)values.Length;
                if (names < 0.6) continue;
                var score = averages[c] / Math.Max(1, repeat);
                if (score < best) { best = score; speaker = c; }
            }
        }

        var result = new List<DialogueEntry>();
        foreach (var row in table)
        {
            var body = Cell(row, text);
            var who = Cell(row, speaker);
            var note = Cell(row, comment);
            var type = Fold(Cell(row, kind));
            if (type.StartsWith("ESCENA", StringComparison.Ordinal) || type.StartsWith("SCENE", StringComparison.Ordinal) ||
                type.StartsWith("CORTE", StringComparison.Ordinal))
            {
                result.Add(new DialogueEntry(DialogueKind.SceneBreak, "", CleanText(body.Length > 0 ? body : who), note));
                continue;
            }
            if (body.Length == 0) continue;
            if (who.Length == 0 && speaker < 0 && !hasHeader)
            {
                result.AddRange(ParseLines(body.Split('\n'), mode).Select(x => note.Length > 0 ? x with { Comment = Join(x.Comment, note, "; ") } : x));
                continue;
            }
            var narration = type.StartsWith("NARRA", StringComparison.Ordinal) || who.Length == 0 || IsNarrator(who);
            string? warning = null;
            var cleanName = who;
            var nameComment = "";
            if (!narration && !ValidName(who, out cleanName, out nameComment))
            {
                warning = "Nombre raro en «quién habla»: revísalo";
                cleanName = who.Length > 40 ? who[..40] : who;
                nameComment = "";
            }
            var entryText = CleanText(body);
            if (LeadingParenthetical.Match(entryText) is { Success: true } lead)
            {
                note = Join(lead.Groups["comment"].Value.Trim(), note, "; ");
                entryText = lead.Groups["text"].Value.Trim();
            }
            if (entryText.Length > MaxTextLength)
                warning = Join(warning, $"Texto de {entryText.Length} caracteres: el TTS puede tardar o fallar; conviene partirlo");
            result.Add(new DialogueEntry(narration ? DialogueKind.Narration : DialogueKind.Dialogue,
                narration ? Narrator : cleanName, entryText, Join(nameComment, note, "; "), warning));
        }
        return result;
    }

    // ───────────────────────────── Files ─────────────────────────────

    /// <summary>.txt/.md (UTF-8, or Latin-1 when it is not UTF-8), .docx (paragraphs and tables in order), .xlsx
    /// (first sheet), .csv/.tsv. Old .doc/.xls are binary formats: save them as .docx/.xlsx first.</summary>
    public static IReadOnlyList<DialogueEntry> ParseFile(string path, DialogueParseMode mode)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".docx" or ".docm" => ParseDocx(File.ReadAllBytes(path), mode),
            ".xlsx" or ".xlsm" => ParseTable(ReadXlsx(File.ReadAllBytes(path)), mode),
            ".csv" or ".tsv" => ParseTable(ReadCsv(ReadText(File.ReadAllBytes(path))), mode),
            ".doc" or ".xls" => throw new InvalidDataException(
                "Los .doc y .xls antiguos no se pueden leer: ábrelos en Word o Excel y guárdalos como .docx o .xlsx."),
            _ => ParseText(ReadText(File.ReadAllBytes(path)), mode)
        };
    }

    /// <summary>Notepad «ANSI» on Spanish Windows: like Latin-1 but with — – “ ” … in 0x80–0x9F.</summary>
    private static readonly Encoding Windows1252 = CreateWindows1252();

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    public static string ReadText(byte[] bytes)
    {
        if (bytes.Length >= 2 && (bytes[0], bytes[1]) is (0xFF, 0xFE)) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && (bytes[0], bytes[1]) is (0xFE, 0xFF)) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try { return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start); }
        catch (DecoderFallbackException) { return Windows1252.GetString(bytes); } // Notepad «ANSI» files
    }

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static IReadOnlyList<DialogueEntry> ParseDocx(byte[] bytes, DialogueParseMode mode)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("El archivo no es un documento de Word (.docx) válido.");
        XDocument document;
        using (var stream = entry.Open()) document = XDocument.Load(stream);
        var body = document.Root?.Element(W + "body") ?? throw new InvalidDataException("El documento de Word está vacío.");
        var result = new List<DialogueEntry>();
        var pending = new List<string>();
        void Flush()
        {
            if (pending.Count == 0) return;
            result.AddRange(ParseLines(pending, mode));
            pending.Clear();
        }
        foreach (var element in body.Elements())
        {
            if (element.Name == W + "p") pending.AddRange(ParagraphText(element).Split('\n'));
            else if (element.Name == W + "tbl")
            {
                var rows = element.Elements(W + "tr").Select(tr => (IReadOnlyList<string>)tr.Elements(W + "tc")
                    .Select(tc => string.Join("\n", tc.Elements(W + "p").Select(ParagraphText))).ToArray()).ToArray();
                if (rows.Length == 0) continue;
                Flush();
                result.AddRange(ParseTable(rows, mode));
            }
            else if (element.Name == W + "sdt")
                foreach (var paragraph in element.Descendants(W + "p")) pending.AddRange(ParagraphText(paragraph).Split('\n'));
        }
        Flush();
        return result;
    }

    private static string ParagraphText(XElement paragraph)
    {
        var text = new StringBuilder();
        foreach (var node in paragraph.Descendants())
        {
            if (node.Name == W + "t") text.Append(node.Value);
            else if (node.Name == W + "tab") text.Append('\t');
            else if (node.Name == W + "br" || node.Name == W + "cr") text.Append('\n');
            else if (node.Name == W + "noBreakHyphen") text.Append('-');
        }
        return text.ToString();
    }

    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Pr = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>The cells of the first worksheet, as text rows (empty cells keep their column).</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ReadXlsx(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        XDocument? Load(string name)
        {
            var entry = zip.GetEntry(name);
            if (entry is null) return null;
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }
        var shared = Load("xl/sharedStrings.xml")?.Root?.Elements(S + "si")
            .Select(si => string.Concat(si.Descendants(S + "t").Where(t => t.Parent?.Name != S + "rPh").Select(t => t.Value))).ToArray() ?? [];
        var sheetPath = "xl/worksheets/sheet1.xml";
        var workbook = Load("xl/workbook.xml");
        var firstSheet = workbook?.Root?.Element(S + "sheets")?.Elements(S + "sheet").FirstOrDefault();
        var relationId = firstSheet?.Attribute(R + "id")?.Value;
        if (relationId is not null && Load("xl/_rels/workbook.xml.rels")?.Root?.Elements(Pr + "Relationship")
                .FirstOrDefault(x => x.Attribute("Id")?.Value == relationId)?.Attribute("Target")?.Value is { Length: > 0 } target)
            sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target.Replace("\\", "/");
        var sheet = Load(sheetPath) ?? throw new InvalidDataException("El libro de Excel no tiene hojas legibles.");
        var rows = new List<IReadOnlyList<string>>();
        foreach (var row in sheet.Descendants(S + "row"))
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements(S + "c"))
            {
                var column = ColumnIndex(cell.Attribute("r")?.Value) ?? cells.Count;
                while (cells.Count < column) cells.Add("");
                var type = cell.Attribute("t")?.Value;
                var value = cell.Element(S + "v")?.Value ?? "";
                var text = type switch
                {
                    "s" when int.TryParse(value, out var index) && index >= 0 && index < shared.Length => shared[index],
                    "inlineStr" => string.Concat(cell.Descendants(S + "t").Select(t => t.Value)),
                    "b" => value == "1" ? "VERDADERO" : "FALSO",
                    _ => value
                };
                if (column < cells.Count) cells[column] = text; else cells.Add(text);
            }
            rows.Add(cells);
        }
        return rows;
    }

    private static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        var index = 0;
        var letters = 0;
        foreach (var c in reference)
        {
            if (c is < 'A' or > 'Z') break;
            index = index * 26 + (c - 'A' + 1);
            letters++;
        }
        return letters == 0 ? null : index - 1;
    }

    /// <summary>CSV/TSV with quotes; the separator (; , or tab) is the most frequent one in the first line.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ReadCsv(string text)
    {
        var firstLine = text.Split('\n')[0];
        var separator = new[] { '\t', ';', ',' }.OrderByDescending(c => firstLine.Count(x => x == c)).First();
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == separator) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n')
            {
                row.Add(cell.ToString().TrimEnd('\r'));
                cell.Clear();
                rows.Add(row);
                row = [];
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString().TrimEnd('\r'));
            rows.Add(row);
        }
        return rows;
    }

    // ───────────────────────────── Export ─────────────────────────────

    /// <summary>The recommended text format; reading it back gives the same rows (narration is written with
    /// «Narrador:» so a colon inside it is not taken for a name).</summary>
    public static string ToText(IEnumerable<DialogueEntry> entries)
    {
        var text = new StringBuilder();
        foreach (var entry in entries)
        {
            var comment = entry.Comment.Trim().Replace('(', '[').Replace(')', ']');
            var body = (comment.Length > 0 ? $"({comment}) " : "") + entry.Text.Replace('\n', ' ').Trim();
            switch (entry.Kind)
            {
                case DialogueKind.SceneBreak:
                    if (text.Length > 0) text.AppendLine();
                    text.AppendLine("[ESCENA] " + entry.Text.Trim());
                    break;
                case DialogueKind.Narration:
                    text.AppendLine(Narrator + ": " + body);
                    break;
                default:
                    text.AppendLine((entry.Speaker.Trim().Length > 0 ? entry.Speaker.Trim() : "NPC") + ": " + body);
                    break;
            }
        }
        return text.ToString();
    }

    // ───────────────────────────── Scenes ─────────────────────────────

    /// <summary>
    /// Consecutive lines between scene cuts; a group longer than <paramref name="maxPerScene"/> is split into equal
    /// parts «Título (1/2)». Scene cuts without lines after them are dropped. Titles default to «Parte N».
    /// </summary>
    public static IReadOnlyList<DialogueScenePart> SplitScenes(IReadOnlyList<DialogueEntry> entries, int maxPerScene = MaxLinesPerScene)
    {
        maxPerScene = Math.Clamp(maxPerScene, 1, MaxLinesPerScene);
        var groups = new List<(string Title, string Notes, List<int> Lines)>();
        (string Title, string Notes, List<int> Lines) current = ("", "", []);
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Kind == DialogueKind.SceneBreak)
            {
                if (current.Lines.Count > 0) groups.Add(current);
                current = (entries[i].Text.Trim(), entries[i].Comment.Trim(), []);
            }
            else current.Lines.Add(i);
        }
        if (current.Lines.Count > 0) groups.Add(current);
        var result = new List<DialogueScenePart>();
        foreach (var group in groups)
        {
            var parts = (int)Math.Ceiling(group.Lines.Count / (double)maxPerScene);
            for (var p = 0; p < parts; p++)
            {
                var from = group.Lines.Count * p / parts;
                var to = group.Lines.Count * (p + 1) / parts;
                var title = group.Title.Length > 0 ? group.Title : $"Parte {result.Count + 1}";
                if (parts > 1) title += $" ({p + 1}/{parts})";
                result.Add(new DialogueScenePart(title, group.Notes, group.Lines.GetRange(from, to - from)));
            }
        }
        return result;
    }

    // ───────────────────────────── AI ─────────────────────────────

    /// <summary>Base of the «Prompt maestro» of the dialogue module (editable per project).</summary>
    public const string BaseInstructions = """
        Eres guionista de videos estilo Loquendo en español: humor, ritmo rápido y frases cortas que suenen bien
        leídas por una voz sintética (TTS). Escribe SOLO los diálogos de la historia, en orden: nada de descripciones
        de planos, cámaras, efectos ni música.
        Reglas:
        - Cada línea la dice UN personaje. Frases de 1 a 3 oraciones; mejor varias líneas cortas que una muy larga.
        - Escribe como se pronuncia: números y siglas en letras si hace falta («dos mil», «a be ce»), sin emojis ni
          símbolos raros, sin acotaciones dentro del texto hablado.
        - Usa los personajes indicados con su forma de hablar. Si hace falta alguien más (un vendedor, un policía…),
          créalo con un nombre corto: se le asignará una voz de NPC.
        - El narrador solo cuando aporte (inicio, saltos de tiempo, remates).
        - Divide la historia en escenas cuando cambie el lugar o el momento, con un título corto.
        - Una acotación breve (tono, a quién le habla) solo cuando cambie cómo se dice la línea.
        """;

    /// <summary>The text format the web AI must answer with (the recommended import format).</summary>
    public const string TextFormatRules = """
        FORMATO DE RESPUESTA (obligatorio, para importarlo tal cual en Loquendo AI):
        - Una línea por intervención: Nombre: texto
        - Acotación opcional entre paréntesis al inicio del texto: Nombre: (enojado) texto
        - Narrador: texto   para la narración.
        - [ESCENA] Título corto   en su propia línea al empezar cada escena (también la primera).
        - Nada más: sin títulos en negrita, sin markdown, sin numerar líneas, sin comentarios antes ni después.
        Ejemplo:
        [ESCENA] En la cocina
        Narrador: Era un lunes cualquiera en Springfield.
        Homero: (bostezando) Marge, ¿dónde están mis donas?
        Marge: Te las comiste anoche, Homero.
        """;

    public const string JsonFormatRules = """
        Responde SOLO con el JSON del esquema. «lineas» en orden. tipo: «escena» (texto = título corto de la escena que
        empieza, personaje vacío), «narracion» (personaje «Narrador») o «dialogo». acotacion: vacía o una indicación
        breve de cómo se dice. Empieza con una línea de tipo «escena».
        """;

    /// <summary>The characters block of the prompt: name and notes of each chosen character, plus extra names.</summary>
    public static string CharactersSection(IEnumerable<(string Name, string Notes)> characters, IEnumerable<string>? extras = null)
    {
        var text = new StringBuilder("PERSONAJES QUE PARTICIPAN (usa estos nombres exactos):\n");
        var any = false;
        foreach (var (name, notes) in characters)
        {
            any = true;
            text.Append("- ").Append(name);
            var clean = Regex.Replace(notes ?? "", @"\s+", " ").Trim();
            if (clean.Length > 0) text.Append(": ").Append(clean.Length > 220 ? clean[..220] + "…" : clean);
            text.Append('\n');
        }
        foreach (var extra in (extras ?? []).Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            any = true;
            text.Append("- ").Append(extra).Append(" (secundario)\n");
        }
        if (!any) text.Append("- (los que necesite la historia; nombres cortos)\n");
        return text.ToString();
    }

    /// <summary>What to paste in a web AI (ChatGPT, Gemini, Claude…): instructions, format, characters and the story.</summary>
    public static string WebPrompt(string instructions, string charactersSection, string story, int lines)
    {
        var text = new StringBuilder();
        text.AppendLine(string.IsNullOrWhiteSpace(instructions) ? BaseInstructions.Trim() : instructions.Trim()).AppendLine();
        text.AppendLine(TextFormatRules.Trim()).AppendLine();
        text.AppendLine(charactersSection.Trim()).AppendLine();
        text.AppendLine($"EXTENSIÓN: unas {Math.Clamp(lines, 4, MaxLines)} líneas en total (máximo {MaxLines}).").AppendLine();
        text.AppendLine("HISTORIA:");
        text.AppendLine(string.IsNullOrWhiteSpace(story) ? "[Escribe aquí de qué trata el video, el tono y cómo termina]" : story.Trim());
        return text.ToString();
    }

    /// <summary>JSON schema of an in-app AI answer.</summary>
    public static object JsonSchema(int maxLines) => new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["lineas"] = new Dictionary<string, object>
            {
                ["type"] = "array",
                ["maxItems"] = Math.Clamp(maxLines, 1, MaxLines),
                ["items"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["tipo"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = new[] { "escena", "narracion", "dialogo" } },
                        ["personaje"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["texto"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["acotacion"] = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    ["required"] = new[] { "tipo", "personaje", "texto", "acotacion" },
                    ["additionalProperties"] = false
                }
            }
        },
        ["required"] = new[] { "lineas" },
        ["additionalProperties"] = false
    };

    /// <summary>Rows of an in-app AI answer; a text answer (models that ignore the schema) is read as the
    /// recommended format.</summary>
    public static IReadOnlyList<DialogueEntry> ParseAiOutput(string output)
    {
        var trimmed = (output ?? "").Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end < start) return ParseText(trimmed, DialogueParseMode.Analysis);
        using var json = JsonDocument.Parse(trimmed[start..(end + 1)]);
        if (!json.RootElement.TryGetProperty("lineas", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("La respuesta de la IA no trae «lineas».");
        var result = new List<DialogueEntry>();
        foreach (var item in list.EnumerateArray())
        {
            string Get(string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? Regex.Replace(value.GetString() ?? "", @"\s+", " ").Trim() : "";
            var type = Fold(Get("tipo"));
            var speaker = Get("personaje");
            var text = CleanText(Get("texto"));
            var comment = Get("acotacion").Trim('(', ')', ' ');
            if (type.StartsWith("ESCENA", StringComparison.Ordinal))
            {
                result.Add(new DialogueEntry(DialogueKind.SceneBreak, "", text.Length > 0 ? text : speaker, comment));
                continue;
            }
            if (text.Length == 0) continue;
            if (type.StartsWith("NARRA", StringComparison.Ordinal) || speaker.Length == 0 || IsNarrator(speaker))
                result.Add(new DialogueEntry(DialogueKind.Narration, Narrator, text, comment));
            else
                result.Add(new DialogueEntry(DialogueKind.Dialogue, ValidName(speaker, out var name, out _) ? name : speaker, text, comment));
        }
        return result;
    }
}

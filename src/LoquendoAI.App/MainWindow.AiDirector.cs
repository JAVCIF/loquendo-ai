using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using LoquendoAI.Core.Models;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private CancellationTokenSource? _aiRequestCancellation;
    private bool _aiRecordedDraft;
    private string? _aiDraftPrompt;
    private int _aiDraftMode;

    private sealed record AiDirectorStep(string Line, string BlockId, string? Note = null, string? Error = null);

    private static string ShortAiContext(string? value, int maxLength)
    {
        var singleLine = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength];
    }

    private static bool DirectorLooksLikeSpriteSheet(AssetRecord asset, IReadOnlyList<AssetTag> tags)
    {
        if (!DirectorImageExtensions.Contains(DirectorAssetExtension(asset))) return false;
        if (tags.Any(t => t.Key == "director.manual.role" &&
            SameDirectorName(t.Value, "render"))) return false;
        var role = TagValue(tags, asset.Id, "role");
        var description = TagValue(tags, asset.Id, "description");
        var path = (asset.SourceRelativePath ?? asset.RelativePath).Replace('\\', '/');
        var basename = Path.GetFileNameWithoutExtension(path);
        var label = NormalizeDirectorName(role + " " + description + " " + basename);
        return label.Contains("SPRITE SHEET", StringComparison.Ordinal) ||
               label.Contains("SPRITESHEET", StringComparison.Ordinal) ||
               label.Contains("HOJA DE SPRITES", StringComparison.Ordinal) ||
               label.Contains("HOJA_DE_SPRITES", StringComparison.Ordinal) ||
               label.Contains("ATLAS", StringComparison.Ordinal) ||
               label.Contains("MINIATURAS", StringComparison.Ordinal) ||
               NormalizeDirectorName(path).Contains("/MINIATURAS/", StringComparison.Ordinal);
    }

    private static bool DirectorMusicIsAmbient(AssetRecord asset, Func<AssetRecord, string, string> label)
    {
        var description = asset.DisplayName + " " + (asset.SourceRelativePath ?? "") + " " +
            label(asset, "role") + " " + label(asset, "mood") + " " + label(asset, "description");
        var normalized = NormalizeDirectorName(description);
        return normalized.Contains("AMBIEN", StringComparison.Ordinal) ||
               normalized.Contains("ATMOSFER", StringComparison.Ordinal) ||
               normalized.Contains("RELAX", StringComparison.Ordinal);
    }

    /// <summary>Last two folders of an asset path: folder names often carry the expression or
    /// pose ("Bart/Enojado") even when the asset has no AI or manual tags yet.</summary>
    private static string DirectorFolderHint(AssetRecord asset)
    {
        var parts = (asset.SourceRelativePath ?? asset.RelativePath).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 1 ? "" : string.Join('/', parts[..^1].TakeLast(2));
    }

    /// <summary>
    /// Chooses the resources offered to the model, per use, so every action type has
    /// candidates. Within each bucket, premise matches come first and the rest is sampled at
    /// random: previously ties were broken by "has an AI description" and then by name, so the
    /// model always saw the same few tagged renders.
    /// </summary>
    private static (AssetRecord[] Catalog, Dictionary<Guid, string> Use) BuildDirectorCatalog(
        IReadOnlyList<AssetRecord> usable, IReadOnlyList<string> words, string prompt,
        Func<AssetRecord, string, string> label, Func<AssetRecord, string> ownerName,
        IReadOnlySet<string> focusCharacters, IReadOnlyDictionary<Guid, string>? pinned = null)
    {
        bool Focus(string name) => prompt.Contains(name, StringComparison.OrdinalIgnoreCase) || focusCharacters.Contains(name);
        var random = new Random();
        var score = usable.ToDictionary(a => a.Id, a => words.Count(word =>
            (a.DisplayName + " " + DirectorFolderHint(a) + " " + label(a, "description") + " " +
             label(a, "subject") + " " + label(a, "mood")).Contains(word, StringComparison.OrdinalIgnoreCase)));
        string Use(AssetRecord a)
        {
            var extension = DirectorAssetExtension(a);
            var role = label(a, "role");
            if (DirectorVideoExtensions.Contains(extension)) return "video";
            if (DirectorAudioExtensions.Contains(extension))
            {
                // Music and SFX stay separate for the AI: a 5-minute track must never be offered
                // as a hit effect. Unclassified sounds go by estimated length, or are left out.
                if (a.Kind == AssetKind.Music) return AudioDurationEstimate.TooShortForMusic(a) ? "otro" : "musica";
                if (a.Kind == AssetKind.SoundEffect) return AudioDurationEstimate.TooLongForEffect(a) ? "otro" : "sfx";
                return AudioDurationEstimate.ClassifyUnlabelled(a) ?? "otro";
            }
            if (!DirectorImageExtensions.Contains(extension)) return "otro";
            if (a.Kind == AssetKind.CharacterSprite || SameDirectorName(role, "render")) return "render";
            if (a.Kind == AssetKind.Background || SameDirectorName(role, "fondo")) return "fondo";
            if (a.Kind is AssetKind.Prop or AssetKind.Meme or AssetKind.VisualEffect or AssetKind.Overlay ||
                SameDirectorName(role, "prop") || SameDirectorName(role, "gif")) return "imagen";
            return "imagen o fondo";
        }
        var use = usable.ToDictionary(a => a.Id, Use);
        // Episode continuity: what was on screen at the end of the previous scene is always offered,
        // with the use it had there (a background stays a background even if it is untagged).
        pinned ??= new Dictionary<Guid, string>();
        foreach (var (id, forced) in pinned)
            if (use.ContainsKey(id)) use[id] = forced;
        IEnumerable<AssetRecord> Pick(IEnumerable<AssetRecord> items, int matched, int total)
        {
            var list = items.ToList();
            var top = list.Where(a => score[a.Id] > 0).OrderByDescending(a => score[a.Id]).Take(matched).ToList();
            return top.Concat(list.Except(top).OrderBy(_ => random.Next()).Take(Math.Max(0, total - top.Count)));
        }
        IEnumerable<AssetRecord> Bucket(params string[] uses) => usable.Where(a => uses.Contains(use[a.Id]));

        var renders = usable.Where(a => use[a.Id] == "render" && ownerName(a).Length > 0)
            .GroupBy(ownerName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => Focus(group.Key))
            .SelectMany(group => Focus(group.Key) ? Pick(group, 4, 10) : Pick(group, 1, 3))
            .Take(40);
        // 1.4.1: a few renders without a registered character, offered as NPCs (extras): the ones the premise
        // names first. Shown with «mostrar» personaje NPC and hidden with «ocultar_npc».
        // NPC renders already in the scene or carried from the previous one (pinned) always go, so they can be hidden.
        var npcRenders = usable.Where(a => pinned.ContainsKey(a.Id) && use[a.Id] == "render" && ownerName(a).Length == 0)
            .Concat(Pick(usable.Where(a => use[a.Id] == "render" && ownerName(a).Length == 0 && !pinned.ContainsKey(a.Id)), 3, 5));
        var ambient = Bucket("musica").Where(a => DirectorMusicIsAmbient(a, label))
            .OrderBy(_ => random.Next()).Take(3);
        var catalog = usable.Where(a => pinned.ContainsKey(a.Id))
            .Concat(usable.Where(a => score[a.Id] > 0 && use[a.Id] != "otro"))
            .OrderByDescending(a => score[a.Id]).Take(10)
            .Concat(renders)
            .Concat(npcRenders)
            .Concat(Pick(Bucket("fondo", "imagen o fondo"), 6, 10))
            .Concat(Pick(Bucket("imagen", "imagen o fondo"), 4, 8))
            .Concat(Pick(Bucket("video"), 2, 4))
            .Concat(ambient)
            .Concat(Pick(Bucket("musica"), 3, 6))
            .Concat(Pick(Bucket("sfx"), 5, 10))
            .DistinctBy(a => a.Id).Take(90).ToArray();
        return (catalog, catalog.ToDictionary(a => a.Id, a => use[a.Id]));
    }

    private IReadOnlyList<AiDirectorStep> NormalizeAiDirectorStep(AiDirectorStep step,
        IReadOnlyDictionary<Guid, AssetRecord> offeredAssets, Func<AssetRecord, string> subject,
        Func<AssetRecord, string> role, bool recorded)
    {
        if (step.BlockId.Length > 0) return [step];
        var line = step.Line.Trim();
        if (!recorded)
        {
            // A pause cannot contain dialogue. Split only when the speaker is
            // an exact registered character and the duration is unambiguous.
            var combined = Regex.Match(line,
                @"^\[PAUSA\]\s*(?<ms>\d+)(?:\s*ms)?\s+y\s+(?<speaker>[^:\r\n]+):\s*(?<text>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (combined.Success)
            {
                var character = _characters.FirstOrDefault(x =>
                    SameDirectorName(x.Name, combined.Groups["speaker"].Value));
                if (character is not null)
                    return
                    [
                        new AiDirectorStep("[PAUSA] " + combined.Groups["ms"].Value, "", "Separado del diálogo"),
                        new AiDirectorStep(character.Name + ": " + combined.Groups["text"].Value.Trim(), "",
                            "Separado de la pausa")
                    ];
            }
        }
        if (!line.StartsWith("[MOSTRAR]", StringComparison.OrdinalIgnoreCase)) return [step];
        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        var correction = new List<string>();
        for (var i = 1; i < parts.Length; i++)
        {
            if (!SameDirectorName(parts[i], "medio")) continue;
            parts[i] = "medio cuerpo";
            correction.Add("encuadre medio cuerpo");
        }
        var renderId = parts[0]["[MOSTRAR]".Length..].Trim();
        if (Guid.TryParse(renderId, out var id) && offeredAssets.TryGetValue(id, out var render) &&
            (render.Kind == AssetKind.CharacterSprite || SameDirectorName(role(render), "render")))
        {
            var renderSubject = string.IsNullOrWhiteSpace(render.SubjectName)
                ? subject(render) : render.SubjectName ?? "";
            var character = _characters.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(renderSubject) && SameDirectorName(x.Name, renderSubject));
            if (character is not null)
            {
                parts[0] = "[MOSTRAR] " + character.Name;
                parts = [.. parts.Take(1), renderId, .. parts.Skip(1)];
                correction.Add("personaje del render");
            }
        }
        return correction.Count == 0 ? [step] :
            [step with { Line = string.Join(" | ", parts), Note = "Ajustado: " + string.Join(", ", correction) }];
    }

    private sealed record AiSceneDraftResult(SceneScriptBlock[] Draft, List<DirectorDraftRow> Rows);

    /// <summary>
    /// Core of the AI Director for ONE scene: catalog, context, model call and validation into
    /// reviewable rows. Used by «Generar borrador» and, scene by scene, by the episode director.
    /// It never writes to the database; callers decide what to apply.
    /// </summary>
    private async Task<AiSceneDraftResult> BuildAiSceneDraftAsync(
        LoquendoAI.Infrastructure.Persistence.SqliteProjectRepository repository, Guid sceneId,
        SceneScriptBlock[] original, string prompt, bool recorded, string provider, string model,
        IProgress<string> progress, CancellationToken token, string purpose, SceneEndState? continuity = null)
    {
        var sources = await repository.GetAssetSourcesAsync();
        // Large libraries (tens of thousands of assets) used to be read and filtered on the
        // UI thread, which froze the window before Ollama was even called.
        var preparation = System.Diagnostics.Stopwatch.StartNew();
        progress.Report("Preparando el catálogo de recursos…");
        var (assetSnapshot, tags) = await Task.Run(
            () => repository.GetDirectorCatalogSnapshotAsync(token), token);
        var assets = assetSnapshot.ToArray();
        var tagsByAsset = await Task.Run(() => tags.GroupBy(x => x.AssetId)
            .ToDictionary(group => group.Key, group => group.ToArray()), token);
        string Label(AssetRecord asset, string field) =>
            TagValue(tagsByAsset.TryGetValue(asset.Id, out var own) ? own : [], asset.Id, field);
        string OwnerName(AssetRecord asset)
        {
            var explicitName = !string.IsNullOrWhiteSpace(asset.SubjectName)
                ? asset.SubjectName : Label(asset, "subject");
            var registered = _characters.FirstOrDefault(x => SameDirectorName(x.Name, explicitName ?? ""));
            if (registered is not null) return registered.Name;
            var folders = (asset.SourceRelativePath ?? "").Replace('\\', '/').Split('/').SkipLast(1);
            return _characters.FirstOrDefault(character =>
                folders.Any(folder => SameDirectorName(folder, character.Name)))?.Name ?? "";
        }
        var usableAssets = await Task.Run(() => assets.Where(asset =>
            !DirectorLooksLikeSpriteSheet(asset,
                tagsByAsset.TryGetValue(asset.Id, out var own) ? own : [])).ToArray(), token);
        var words = prompt.Split([' ', '\r', '\n', ',', '.', ':', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length > 3).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToArray();
        var typed = !DirectorAiGrammar.UseLegacyOutput;
        // In recorded mode the speakers come from the takes, not from the premise: they get
        // the same wide choice of renders as a character named in the prompt.
        var speakingNames = original.Where(IsRecordedTake).Select(b => _characters.FirstOrDefault(x => x.Id == b.CharacterId)?.Name)
            .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pinned = new Dictionary<Guid, string>();
        // NPC renders already in the scene (recorded mode keeps them): offered so the AI can hide them.
        foreach (var shown in original.Where(x => x.Kind == ScriptBlockKind.CharacterShow && x.CharacterId is null && x.AssetId is not null))
            pinned[shown.AssetId!.Value] = "render";
        if (continuity is not null)
        {
            if (continuity.BackgroundId is Guid background) pinned[background] = "fondo";
            foreach (var shown in continuity.OnScreen) pinned[shown.RenderId] = "render";
            if (continuity.MusicId is Guid music) pinned[music] = "musica";
        }
        var (catalog, use) = await Task.Run(() => BuildDirectorCatalog(usableAssets, words, prompt, Label, OwnerName, speakingNames,
            pinned), token);

        var aliasById = catalog.Select((asset, index) => (asset.Id, Alias: $"A{index + 1}"))
            .ToDictionary(item => item.Id, item => item.Alias);
        var idByAlias = aliasById.ToDictionary(item => item.Value, item => item.Key,
            StringComparer.OrdinalIgnoreCase);
        var blockIdByRef = original.Select((block, index) => (Ref: $"B{index + 1}", block.Id))
            .ToDictionary(item => item.Ref, item => item.Id, StringComparer.OrdinalIgnoreCase);
        var context = new StringBuilder();
        context.AppendLine($"MODO: {(recorded ? "montar voces" : "generar guion")}. Recursos disponibles ({catalog.Length}/{usableAssets.Length}); hojas de sprites excluidas: {assets.Length - usableAssets.Length}." +
            (typed ? " Usa las referencias A1, A2… en el campo recurso." : " Copia solo referencias A1, A2... en line."));
        foreach (var asset in catalog)
            context.AppendLine($"ref={aliasById[asset.Id]}; uso={use[asset.Id]}; nombre={ShortAiContext(asset.DisplayName, 65)}; " +
                $"carpeta={ShortAiContext(DirectorFolderHint(asset), 45)}; " +
                $"sujeto={ShortAiContext(OwnerName(asset).DefaultIfEmpty(Label(asset, "subject").DefaultIfEmpty(asset.SubjectName)), 45)}; " +
                (use[asset.Id] is "musica" or "sfx" ? $"duracion≈{AudioDurationEstimate.Label(AudioDurationEstimate.Seconds(asset).Typical)}; " : "") +
                $"expresión={ShortAiContext(Label(asset, "mood"), 32)}; " +
                $"descripcion={ShortAiContext(Label(asset, "description"), 95)}");
        bool HasVoice(CharacterDefinition character) =>
            character.DefaultVoiceProfileId is Guid voice && _voiceProfiles.Any(x => x.Id == voice);
        context.AppendLine("Personajes registrados:");
        foreach (var character in _characters.OrderByDescending(x => speakingNames.Contains(x.Name)).Take(70))
            context.AppendLine(recorded
                ? $"{character.Name} ({(speakingNames.Contains(character.Name) ? "habla en las tomas" : "no habla en las tomas")}; " +
                  $"notas={ShortAiContext(character.Notes, 100)})"
                : $"{character.Name} (voz={(HasVoice(character) ? "configurada" : "sin perfil")}; " +
                  $"notas={ShortAiContext(character.Notes, 100)})");
        var offeredAssets = catalog.ToDictionary(x => x.Id);
        // Renders per registered character: listed as pairs for the legacy line format and
        // turned into one schema branch per character for the typed format.
        var rendersByCharacter = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in catalog.Where(a => use[a.Id] == "render"))
        {
            // A render shown for a character in the previous scene stays that character's render.
            var shownAs = continuity?.OnScreen.FirstOrDefault(x => x.RenderId == asset.Id)?.Character;
            var owner = _characters.FirstOrDefault(x => SameDirectorName(x.Name, shownAs ?? OwnerName(asset)));
            // A render of nobody registered is an NPC render (1.4.1), unless a character is really called «NPC».
            var key = owner?.Name ?? DirectorAiSchema.NpcKey;
            if (owner is null && _characters.Any(x => SameDirectorName(x.Name, DirectorAiSchema.NpcKey))) continue;
            if (!rendersByCharacter.TryGetValue(key, out var list)) rendersByCharacter[key] = list = [];
            list.Add(aliasById[asset.Id]);
        }
        // Claude and ChatGPT get the compact schema (see DirectorAiSchema.Build): the render ↔ character pairs
        // travel in the prompt instead of the schema, and CheckRefs validates the answer.
        var compactSchema = typed && provider is "claude" or "openai";
        if (compactSchema)
        {
            context.AppendLine("Renders por personaje (en «mostrar», el recurso debe ser uno de SU lista):");
            foreach (var (name, refs) in rendersByCharacter)
                context.AppendLine($"{name}: {string.Join(", ", refs)}");
        }
        if (!typed)
        {
            context.AppendLine("Renders que corresponden a personajes (solo los siguientes pares son verificables):");
            foreach (var (name, refs) in rendersByCharacter)
                foreach (var reference in refs) context.AppendLine($"[MOSTRAR] {name} | {reference} | medio cuerpo");
        }
        if (continuity is not null && ContinuityLine(continuity, aliasById) is { Length: > 0 } continuityLine)
            context.AppendLine(continuityLine);
        if (recorded)
        {
            context.AppendLine(typed
                ? "Bloques originales; incluye CADA uno exactamente una vez con la acción conservar:"
                : "Bloques existentes, de los que debes conservar CADA ID exactamente una vez:");
            for (var i = 0; i < original.Length; i++)
            {
                var block = original[i];
                context.AppendLine($"{(typed ? $"ref=B{i + 1}" : $"ID={block.Id:D}")}; tipo={block.Kind}; texto={block.Text}; " +
                    $"personaje={_characters.FirstOrDefault(x => x.Id == block.CharacterId)?.Name ?? ""}; " +
                    $"duracionWavMs={(IsImportedVoice(block) ? block.GeneratedDurationMs : null)}" +
                    // Acotación from the dialogue module (how the line is said): helps choose expression and gestures.
                    (LoquendoAI.Infrastructure.Composition.BlockParameters.Of(block).Extra.TryGetValue("acotacion", out var note) && note.ValueKind == JsonValueKind.String
                        ? $"; acotacion={ShortAiContext(note.GetString() ?? "", 80)}" : "") +
                    (typed ? "" : $"; assetId={block.AssetId}"));
            }
        }
        // VEGAS catalog transitions: a short, varied list per request (favourites, premise
        // keywords, well-known ones). Only used by the VEGAS export; the preview shows a crossfade.
        var pluginByRef = new Dictionary<string, DirectorPluginRef>(StringComparer.OrdinalIgnoreCase);
        if (typed)
        {
            IReadOnlyList<LoquendoAI.Infrastructure.Composition.VegasTransitionCatalog.PluginOption> pluginOptions = [];
            try { pluginOptions = LoquendoAI.Infrastructure.Composition.VegasTransitionCatalog.OptionsForDirector(prompt, 12); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or UnauthorizedAccessException)
            {
                AiDiagnostics.Note("director: catálogo de transiciones VEGAS no disponible: " + ex.Message);
            }
            if (pluginOptions.Count > 0)
            {
                context.AppendLine("Transiciones de VEGAS (se ven al exportar a VEGAS; 1 a 3 momentos clave: acción transicion estilo cruce con vegas=Tn antes del nuevo fondo o personaje):");
                for (var i = 0; i < pluginOptions.Count; i++)
                {
                    var option = pluginOptions[i];
                    pluginByRef[$"T{i + 1}"] = new DirectorPluginRef(option.Id, option.Preset);
                    context.AppendLine($"T{i + 1} = {ShortAiContext(option.Name, 50)} · {ShortAiContext(option.Preset, 40)} [{option.Vendor}]");
                }
            }
        }
        string[] Refs(Func<string, bool> accepts) =>
            catalog.Where(a => accepts(use[a.Id])).Select(a => aliasById[a.Id]).ToArray();
        var schemaContext = typed
            ? new DirectorSchemaContext(
                Refs(x => x is "fondo" or "imagen o fondo"),
                Refs(x => x is "imagen" or "imagen o fondo"),
                Refs(x => x == "video"),
                Refs(x => x == "musica"),
                Refs(x => x == "sfx"),
                rendersByCharacter.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value),
                _characters.Where(HasVoice).Select(x => x.Name).ToArray(),
                rendersByCharacter.Keys.Where(x => !SameDirectorName(x, DirectorAiSchema.NpcKey)).ToArray(),
                blockIdByRef.Keys.ToArray(),
                recorded,
                pluginByRef.Keys.ToArray())
            : null;
        object schema = schemaContext is not null
            ? DirectorAiSchema.Build(schemaContext, compactSchema)
            : recorded
                ? OllamaDirectorClient.RecordedSceneSchema(original.Select(x => x.Id.ToString("D")).ToArray())
                : OllamaDirectorClient.StorySchema;

        AiDiagnostics.Note($"director: preparación {preparation.ElapsedMilliseconds:N0} ms | " +
            $"biblioteca {assets.Length:N0} assets ({usableAssets.Length:N0} usables) | catálogo enviado {catalog.Length} " +
            $"({rendersByCharacter.Sum(x => x.Value.Count)} renders de {rendersByCharacter.Count} personajes, {pluginByRef.Count} transiciones VEGAS) | " +
            $"contexto {context.Length:N0} car | premisa {prompt.Length:N0} car | modo {(recorded ? "voces grabadas" : "historia")} | " +
            $"salida {(typed ? compactSchema ? "tipada compacta" : "tipada" : "líneas (legacy)")}");
        progress.Report(provider switch
        {
            "gemini" => "Gemini está creando el borrador…",
            "claude" => "Claude está creando el borrador…",
            "openai" => "ChatGPT está creando el borrador…",
            _ => "Ollama está creando un borrador local…"
        });
        var output = await GenerateAiResponseAsync(provider, model,
            typed ? DirectorAiGrammar.ForTypedMode(recorded, _directorMasterPrompt)
                  : DirectorAiGrammar.ForMode(recorded, _directorMasterPrompt),
            "ENCARGO DEL USUARIO:\n" + prompt + "\n\nCATÁLOGO Y ESCENA:\n" + context,
            schema, null, token, progress,
            new OllamaChatOptions(6144, purpose));
        token.ThrowIfCancellationRequested();
        using var result = JsonDocument.Parse(output);
        var rawSteps = result.RootElement.GetProperty("steps").EnumerateArray().Select(step =>
        {
            if (!typed)
                return new AiDirectorStep(
                    ResolveAiAssetAliases(step.GetProperty("line").GetString()?.Trim() ?? "", idByAlias),
                    recorded && step.TryGetProperty("blockId", out var blockId) ? blockId.GetString()?.Trim() ?? "" : "");
            var (line, blockRef) = DirectorAiSchema.ToLine(step, pluginByRef);
            return new AiDirectorStep(ResolveAiAssetAliases(line, idByAlias),
                blockRef.Length == 0 ? "" : blockIdByRef.TryGetValue(blockRef, out var blockGuid) ? blockGuid.ToString("D") : blockRef,
                Error: DirectorAiSchema.CheckRefs(step, schemaContext!));
        }).ToArray();
        var steps = rawSteps
            .SelectMany(step => NormalizeAiDirectorStep(step, offeredAssets,
                OwnerName, asset => Label(asset, "role"), recorded)).ToArray();
        if (steps.Length is < 1 or > 120)
            throw new InvalidDataException("La IA devolvió un número de acciones fuera del límite (1 a 120).");
        var originalById = original.ToDictionary(x => x.Id);
        // Camera lines are checked against the order of the whole draft (who is on screen when).
        var cameraChecks = CameraChecks(steps.Select(step => step.BlockId.Length > 0 ? null
            : ParseDirectorPrompt(step.Line).FirstOrDefault()).ToArray());
        var seen = new HashSet<Guid>();
        var draft = new List<SceneScriptBlock>();
        var rows = new List<DirectorDraftRow>();
        foreach (var step in steps)
        {
            var line = step.Line;
            var reference = step.BlockId;
            if (reference.Length > 0)
            {
                if (!recorded || line.Length > 0 || !Guid.TryParse(reference, out var id) ||
                    !originalById.TryGetValue(id, out var existing) || !seen.Add(id))
                    throw new InvalidDataException("La IA repitió o inventó un ID de bloque. Genera el borrador otra vez.");
                draft.Add(existing with { OrderIndex = draft.Count, StartOffsetMs = null });
                rows.Add(new DirectorDraftRow(rows.Count + 1, DirectorKindName(existing.Kind),
                    _characters.FirstOrDefault(x => x.Id == existing.CharacterId)?.Name ?? "Conservado",
                    existing.Text, "Bloque original", IsImportedVoice(existing) ? "WAV original intacto" : "Bloque original intacto", true));
                continue;
            }
            if (line.Length == 0 || line.Length > 700 || line.Contains('\n') || line.Contains('\r'))
                throw new InvalidDataException("La IA devolvió una instrucción vacía o con varias líneas.");
            var specs = ParseDirectorPrompt(line);
            if (specs.Count != 1) throw new InvalidDataException("Una acción de IA contiene varias instrucciones.");
            var spec = specs[0];
            var status = spec.Error ?? step.Error;
            string? cameraNote = null;
            if (cameraChecks.TryGetValue(rows.Count, out var cameraCheck))
            {
                if (cameraCheck.IsError) status ??= cameraCheck.Message;
                else cameraNote = cameraCheck.Message;
            }
            if (recorded && (spec.Kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration))
                status ??= "Las voces grabadas conservan el texto; no se admiten diálogos nuevos";
            if (spec.Kind == ScriptBlockKind.Narration)
                status ??= "Para narración generada se necesita una voz; usa diálogo con personaje y perfil";
            CharacterDefinition? character = null;
            AssetRecord? asset = null;
            if (status is null && spec.CharacterName.Length > 0)
            {
                character = _characters.FirstOrDefault(x => SameDirectorName(x.Name, spec.CharacterName));
                if (character is null && !IsNpcRender(spec)) status = Guid.TryParse(spec.CharacterName, out _)
                    ? "El render necesita un personaje: [MOSTRAR] Nombre | ID real del render"
                    : "Personaje no registrado";
            }
            string? npcNote = null;
            if (status is null && character is null && IsNpcRender(spec))
            {
                var npc = await CheckNpcRenderAsync(repository, sources, spec, original.Concat(draft));
                status = npc.Status;
                asset = npc.Asset;
                npcNote = npc.Note;
            }
            if (status is null && RelevantAssetKinds(spec.Kind).Count > 0)
            {
                if (!Guid.TryParse(spec.ResourceQuery.Trim(), out var offeredId))
                    status = "Usa el ID del catálogo para este recurso";
                else if (!offeredAssets.ContainsKey(offeredId))
                    status = "ID ausente de los recursos mostrados a la IA; no se incorporará";
                else
                {
                    var match = await FindDirectorAssetAsync(repository, sources, spec.Kind, spec.ResourceQuery, character);
                    asset = match.Asset;
                    if (asset is null) status = match.Issue ?? "Recurso no catalogado";
                    else
                    {
                        var role = Label(asset, "role");
                        if (spec.Kind == ScriptBlockKind.CharacterShow &&
                            asset.Kind == AssetKind.Background &&
                            !role.Equals("render", StringComparison.OrdinalIgnoreCase))
                            status = "Este recurso figura como fondo, no como render";
                        if (spec.Kind == ScriptBlockKind.Background &&
                            asset.Kind == AssetKind.CharacterSprite &&
                            !role.Equals("fondo", StringComparison.OrdinalIgnoreCase))
                            status = "Este recurso figura como render, no como fondo";
                        if (spec.Kind == ScriptBlockKind.CharacterShow &&
                            asset.SubjectName is { Length: > 0 } subject && character is not null &&
                            !SameDirectorName(subject, character.Name))
                            status = "El render pertenece a otro personaje";
                        if (spec.Kind == ScriptBlockKind.SoundEffect && (asset.Kind == AssetKind.Music ||
                                AudioDurationEstimate.TooLongForEffect(asset) ||
                                asset.Kind is not AssetKind.SoundEffect && AudioDurationEstimate.ClassifyUnlabelled(asset) == "musica"))
                            status = $"Parece música (≈{AudioDurationEstimate.Label(AudioDurationEstimate.Seconds(asset).Typical)}), no un efecto: elige un SFX";
                        if (spec.Kind == ScriptBlockKind.Music && (asset.Kind == AssetKind.SoundEffect ||
                                AudioDurationEstimate.TooShortForMusic(asset) ||
                                asset.Kind is not AssetKind.Music && AudioDurationEstimate.ClassifyUnlabelled(asset) == "sfx"))
                            status = $"Parece un efecto corto (≈{AudioDurationEstimate.Label(AudioDurationEstimate.Seconds(asset).Typical)}), no música: elige otra pista";
                        if (spec.Kind == ScriptBlockKind.Music &&
                            Regex.IsMatch(prompt, @"\b(ambiental|ambiente|ambient)\b",
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                            !DirectorMusicIsAmbient(asset, Label))
                            status = "Música ambiental sin identificar: etiqueta otra pista como ambiente o corrige la fila";
                        _scriptAssetCache[asset.Id] = asset;
                    }
                }
            }
            draft.Add(new SceneScriptBlock(Guid.NewGuid(), sceneId, draft.Count, spec.Kind,
                CharacterId: character?.Id, Text: spec.Text, AssetId: asset?.Id,
                VoiceProfileId: spec.Kind == ScriptBlockKind.Dialogue ? character?.DefaultVoiceProfileId : null,
                PauseAfterMs: spec.PauseMs, ParametersJson: DirectorParameters(spec, CharacterIdByName)));
            rows.Add(new DirectorDraftRow(rows.Count + 1, DirectorKindName(spec.Kind),
                character is not null && asset is not null ? $"{character.Name} · {asset.DisplayName}"
                    : character?.Name ?? asset?.DisplayName ?? (spec.ResourceQuery.Length > 0 ? spec.ResourceQuery : spec.CharacterName), spec.Text, spec.Summary,
                status ?? npcNote ?? cameraNote ?? step.Note ?? "Listo", status is null) { Instruction = line });
        }
        if (recorded)
        {
            var missing = original.Where(x => !seen.Contains(x.Id)).ToArray();
            foreach (var item in missing)
                rows.Add(new DirectorDraftRow(rows.Count + 1, "Bloque original", item.Id.ToString("D"),
                    item.Text, "Conservar", "La IA omitió este bloque; vuelve a generar", false));
        }
        return new AiSceneDraftResult(draft.ToArray(), rows);
    }

    private void AiCancel_Click(object sender, RoutedEventArgs e) => _aiRequestCancellation?.Cancel();

    private async void AiGenerateDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_aiRequestCancellation is not null || !DirectorDraftButton.IsEnabled) return;
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow scene)
        {
            AiDirectorStatusText.Text = "Abre un proyecto y selecciona una escena.";
            return;
        }
        var prompt = AiDirectorPromptBox.Text.Trim();
        var model = (AiDirectorModelBox.SelectedItem as AiModelChoice)?.Model ?? "";
        var provider = (AiDirectorModelBox.SelectedItem as AiModelChoice)?.Provider ?? "ollama";
        if (prompt.Length is < 8 or > 12000 || model.Length == 0)
        {
            AiDirectorStatusText.Text = "Escribe una premisa (8 a 12 000 caracteres) y selecciona un modelo del proveedor elegido.";
            return;
        }
        var recorded = AiDirectorModeCombo.SelectedIndex == 1;
        var repository = _currentRepository;
        var sceneId = scene.Scene.Id;
        _directorSceneId = null;
        _directorDraftValidated = false;
        AiApplyButton.IsEnabled = DirectorApplyButton.IsEnabled = false;
        AiDraftGrid.ItemsSource = DirectorDraftGrid.ItemsSource = null;
        using var cancellation = new CancellationTokenSource();
        _aiRequestCancellation = cancellation;
        AiGenerateButton.IsEnabled = false;
        DirectorDraftButton.IsEnabled = false;
        AiCancelButton.IsEnabled = AssetCancelAnalysisButton.IsEnabled = true;
        try
        {
            var original = (await repository.GetSceneScriptBlocksAsync(sceneId)).ToArray();
            if (recorded && !original.Any(IsRecordedTake))
                throw new InvalidOperationException("Incorpora primero los WAV a esta escena en Voces grabadas.");
            if (recorded && original.Length > 65)
                throw new InvalidOperationException("Divide la escena: este borrador admite hasta 65 bloques existentes.");
            if (!recorded && _characters.Count == 0)
                throw new InvalidOperationException("Crea al menos un personaje en Voice Lab (sin perfil propio habla con la voz NPC).");

            var status = new Progress<string>(text =>
            {
                if (ReferenceEquals(_aiRequestCancellation, cancellation)) AiDirectorStatusText.Text = text;
            });
            var (draftBlocks, rows) = await BuildAiSceneDraftAsync(repository, sceneId, original, prompt, recorded,
                provider, model, status, cancellation.Token, recorded ? "director-voces" : "director-historia");
            var draft = draftBlocks.ToList();
            // Check again after resolving resource paths asynchronously.
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId ||
                !(await repository.GetSceneScriptBlocksAsync(sceneId)).SequenceEqual(original))
                throw new OperationCanceledException("La escena cambió; genera otro borrador.");
            _directorOriginal = original;
            _directorDraft = draft.ToArray();
            _directorSceneId = sceneId;
            _directorPrompt = DirectorPromptBox.Text;
            _aiRecordedDraft = recorded;
            _aiDraftPrompt = AiDirectorPromptBox.Text;
            _aiDraftMode = AiDirectorModeCombo.SelectedIndex;
            DirectorReplaceCheck.IsChecked = recorded;
            DirectorReplaceCheck.IsEnabled = !recorded;
            AiDraftGrid.ItemsSource = DirectorDraftGrid.ItemsSource = rows;
            var issues = rows.Count(x => !x.IsReady);
            _directorDraftValidated = issues == 0 && draft.Count > 0;
            AiApplyButton.IsEnabled = DirectorApplyButton.IsEnabled = _directorDraftValidated;
            AiDirectorStatusText.Text = issues == 0
                ? $"{draft.Count} bloques listos. Revisa el borrador y aplícalo cuando quieras." 
                : $"{issues} problema(s) en el borrador. Corrige recursos o perfiles y vuelve a generar.";
            DirectorStatusText.Text = AiDirectorStatusText.Text;
        }
        catch (OperationCanceledException) { AiDirectorStatusText.Text = "Generación cancelada; el guion permanece intacto."; }
        catch (Exception ex)
        {
            AiDirectorStatusText.Text = ex.Message +
                (ex is OllamaStallException ? $" (detalles en {AiDiagnostics.PathName})" : "");
            ErrorLog.Record(ex);
        }
        finally
        {
            _aiRequestCancellation = null;
            AiGenerateButton.IsEnabled = true;
            DirectorDraftButton.IsEnabled = true;
            AiCancelButton.IsEnabled = AssetCancelAnalysisButton.IsEnabled = false;
        }
    }
}

internal static class DirectorTagText
{
    public static string DefaultIfEmpty(this string value, string? other) =>
        string.IsNullOrWhiteSpace(value) ? other ?? "" : value;
}

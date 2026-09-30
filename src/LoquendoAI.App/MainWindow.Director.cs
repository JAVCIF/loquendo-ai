using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private Guid? _directorSceneId;
    private string? _directorPrompt;
    private SceneScriptBlock[] _directorOriginal = [];
    private SceneScriptBlock[] _directorDraft = [];
    private bool _directorDraftValidated;

    private void UpdateDirectorDraftTarget(bool updateStatus = true)
    {
        if (DirectorApplyButton is null || AiApplyButton is null) return;
        var selected = (ScenesList.SelectedItem as SceneScriptRow)?.Scene;
        var ready = _currentRepository is not null && selected is not null &&
            _directorSceneId.HasValue && _directorDraft.Length > 0 && _directorDraftValidated;
        DirectorApplyButton.IsEnabled = ready;
        AiApplyButton.IsEnabled = ready && _aiDraftPrompt is not null;
        if (updateStatus && _directorSceneId is not null && selected is not null && _directorDraftValidated)
        {
            var message = selected.Id == _directorSceneId
                ? $"Borrador listo para «{selected.Title}». Puedes aplicarlo aquí."
                : $"Borrador conservado. Se aplicará a «{selected.Title}»; la escena original no cambia.";
            DirectorStatusText.Text = message;
            if (_aiDraftPrompt is not null) AiDirectorStatusText.Text = message;
        }
    }

    private async void DirectorDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_aiRequestCancellation is not null) return;
        _aiRecordedDraft = false;
        _aiDraftPrompt = null;
        AiApplyButton.IsEnabled = false;
        AiDraftGrid.ItemsSource = null;
        DirectorReplaceCheck.IsEnabled = true;
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow selectedScene)
        {
            DirectorStatusText.Text = "Selecciona un proyecto y una escena.";
            return;
        }
        var repository = _currentRepository;
        var sceneId = selectedScene.Scene.Id;
        var prompt = DirectorPromptBox.Text;
        if (prompt.TrimStart().StartsWith('{'))
        {
            await PrepareCopiedSceneAsync(repository, sceneId, prompt);
            return;
        }
        var specs = ParseDirectorPrompt(prompt);
        DirectorApplyButton.IsEnabled = false;
        _directorSceneId = null;
        _directorDraftValidated = false;
        DirectorDraftGrid.ItemsSource = null;
        AiDraftGrid.ItemsSource = null;
        if (specs.Count == 0)
        {
            DirectorStatusText.Text = "Escribe al menos una instrucción, por ejemplo [FONDO] habitación o Bart: Hola.";
            return;
        }

        DirectorDraftButton.IsEnabled = false;
        DirectorStatusText.Text = "Buscando personajes y recursos catalogados…";
        try
        {
            var original = (await repository.GetSceneScriptBlocksAsync(sceneId)).ToArray();
            var sources = await repository.GetAssetSourcesAsync();
            var results = new List<DirectorDraftRow>();
            var blocks = new List<SceneScriptBlock>();
            var inputLines = prompt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#')).ToArray();
            var cameraChecks = CameraChecks(specs);
            foreach (var spec in specs)
            {
                CharacterDefinition? character = null;
                AssetRecord? asset = null;
                var status = spec.Error ?? CinemaNamesIssue(spec);
                string? resourceNote = null;
                if (cameraChecks.TryGetValue(results.Count, out var cameraCheck))
                {
                    if (cameraCheck.IsError) status ??= cameraCheck.Message;
                    else resourceNote = cameraCheck.Message;
                }
                if (status is null && spec.CharacterName.Length > 0)
                {
                    character = _characters.FirstOrDefault(x => SameDirectorName(x.Name, spec.CharacterName));
                    if (character is null && !IsNpcLine(spec) && !IsNpcRender(spec)) status = "Personaje sin registrar";
                }
                if (status is null && character is null && IsNpcRender(spec))
                {
                    var npc = await CheckNpcRenderAsync(repository, sources, spec, original.Concat(blocks));
                    status = npc.Status;
                    asset = npc.Asset;
                    resourceNote = npc.Note;
                }
                if (status is null && RelevantAssetKinds(spec.Kind).Count > 0)
                {
                    if (spec.ResourceQuery.Length == 0 && character is null)
                        status = "Falta nombre del recurso";
                    else
                    {
                        var match = await FindDirectorAssetAsync(repository, sources, spec.Kind, spec.ResourceQuery, character);
                        asset = match.Asset;
                        if (asset is null) status = match.Issue ?? "Recurso sin catalogar";
                        else
                        {
                            _scriptAssetCache[asset.Id] = asset;
                            resourceNote = match.Note;
                        }
                    }
                }
                var parameters = DirectorParameters(spec, CharacterIdByName);
                var block = new SceneScriptBlock(Guid.NewGuid(), sceneId, blocks.Count, spec.Kind,
                    character?.Id, spec.Text, asset?.Id, PauseAfterMs: spec.PauseMs, ParametersJson: parameters);
                blocks.Add(block);
                results.Add(new DirectorDraftRow(results.Count + 1, DirectorKindName(spec.Kind),
                    character?.Name ?? (asset is null ? (spec.ResourceQuery.Length > 0 ? spec.ResourceQuery : spec.CharacterName) : asset.DisplayName + DirectorAssetExtension(asset)), spec.Text, spec.Summary,
                    status ?? resourceNote ?? (asset is null ? "Listo" : "Recurso sugerido"), status is null)
                { Instruction = inputLines[results.Count] });
            }

            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId)
            {
                DirectorStatusText.Text = "La escena cambió. Prepara el borrador de nuevo.";
                return;
            }
            _directorSceneId = sceneId;
            _directorPrompt = prompt;
            _directorOriginal = original;
            _directorDraft = blocks.ToArray();
            DirectorDraftGrid.ItemsSource = results;
            var issues = results.Count(x => !x.IsReady);
            _directorDraftValidated = issues == 0 && blocks.Count > 0;
            DirectorApplyButton.IsEnabled = _directorDraftValidated;
            DirectorStatusText.Text = issues == 0
                ? $"{blocks.Count} bloques preparados. Revisa el borrador y aplica cuando te sirva."
                : $"{issues} elemento(s) pendientes. Ajusta el prompt o cataloga los recursos y vuelve a prepararlo.";
        }
        catch (Exception ex)
        {
            _directorSceneId = null;
            ShowError(ex);
        }
        finally { DirectorDraftButton.IsEnabled = true; }
    }

    private async void DirectorApply_Click(object sender, RoutedEventArgs e)
    {
        var repository = _currentRepository;
        // Every message of «Aplicar» goes to both status bars (Director IA and Director prompt, 1.4.4).
        void Status(string text) => DirectorStatusText.Text = AiDirectorStatusText.Text = text;
        if (repository is null || _directorSceneId is not Guid sourceSceneId ||
            (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id is not Guid targetSceneId ||
            _directorDraft.Length == 0 || !_directorDraftValidated)
        {
            Status("Selecciona una escena y valida el borrador antes de aplicarlo.");
            return;
        }
        DirectorApplyButton.IsEnabled = false;
        AiApplyButton.IsEnabled = false;
        try
        {
            var current = (await repository.GetSceneScriptBlocksAsync(targetSceneId)).ToArray();
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != targetSceneId)
            {
                Status("La escena cambió durante la aplicación. El borrador sigue disponible.");
                return;
            }
            // The rules live in DraftApplyPolicy (tested): empty scene → apply; continuation over a changed scene →
            // say how many blocks are added; «Sustituir» → always ask; voices draft over a changed scene → refuse.
            var decision = DraftApplyPolicy.Decide(current, _directorOriginal, _directorDraft.Length,
                targetSceneId == sourceSceneId, _aiRecordedDraft, DirectorReplaceCheck.IsChecked == true,
                (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Title ?? "");
            if (decision.Action == DraftApplyAction.Refuse)
            {
                Status(decision.Message);
                return;
            }
            if (decision.Action is DraftApplyAction.ConfirmAppend or DraftApplyAction.ConfirmReplace &&
                MessageBox.Show(this, decision.Message,
                    decision.Replace ? "Sustituir el guion" : "Agregar al guion", MessageBoxButton.YesNo,
                    decision.Replace ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                Status("No se aplicó. El borrador sigue disponible.");
                return;
            }
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != targetSceneId)
                return;
            SceneScriptBlock[] keep = decision.Replace ? [] : current;
            if (keep.Length == 0 && targetSceneId == sourceSceneId)
            {
                var draftIds = _directorDraft.Select(x => x.Id).ToHashSet();
                var lostTakes = current.Count(x => IsRecordedTake(x) && !draftIds.Contains(x.Id));
                if (lostTakes > 0 && MessageBox.Show(this,
                        $"Sustituir los bloques quitará {lostTakes} voz(es) grabada(s) de esta escena. Los WAV siguen en el proyecto, " +
                        "pero la escena dejará de usarlos. ¿Continuar?",
                        "Voces grabadas", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
            }
            // A block of the draft that is still in the scene (a continuation over a changed scene) gets a new id,
            // so the scene never holds the same id twice.
            var kept = keep.Select(x => x.Id).ToHashSet();
            var draft = _directorDraft.Select(block => kept.Contains(block.Id) ? block with { Id = Guid.NewGuid() } : block);
            var combined = keep.Concat(draft).Select((block, index) => block with
            {
                Id = targetSceneId == sourceSceneId ? block.Id : Guid.NewGuid(),
                SceneId = targetSceneId, OrderIndex = index, StartOffsetMs = null
            }).ToArray();
            await repository.ReplaceSceneScriptBlocksAsync(targetSceneId, combined);
            _directorSceneId = null;
            _directorDraftValidated = false;
            _aiDraftPrompt = null;
            _aiRecordedDraft = false;
            DirectorReplaceCheck.IsEnabled = true;
            await LoadBlocksAsync(targetSceneId);
            await RefreshSceneTimingAsync(_scriptBlocks);
            DirectorStatusText.Text = $"{_directorDraft.Length} bloques incorporados. Pulsa ▶ en Preview de escena para generar el MP4.";
            AiDirectorStatusText.Text = DirectorStatusText.Text;
        }
        catch (Exception ex) { ShowError(ex); }
        finally { UpdateDirectorDraftTarget(updateStatus: false); }
    }

    private sealed record DirectorAssetMatch(AssetRecord? Asset, string? Issue = null, string? Note = null);

    private static readonly HashSet<string> DirectorImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };
    private static readonly HashSet<string> DirectorVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".mpg", ".mpeg", ".m4v", ".ts", ".m2ts"
    };

    private static string DirectorAssetExtension(AssetRecord asset) =>
        asset.Extension.Length > 0 ? asset.Extension : Path.GetExtension(asset.SourceRelativePath ?? asset.RelativePath);

    private static bool DirectorAssetCompatible(ScriptBlockKind kind, AssetRecord asset) => kind switch
    {
        ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image =>
            DirectorImageExtensions.Contains(DirectorAssetExtension(asset)),
        ScriptBlockKind.Video => DirectorVideoExtensions.Contains(DirectorAssetExtension(asset)),
        // A sound is usable as music or SFX whatever its catalogue category: audio found in a
        // folder without hints is "Sin tipo", and a short jingle catalogued as music can be a SFX.
        // Validating by category left AI rows and manual replacements unresolved.
        ScriptBlockKind.SoundEffect or ScriptBlockKind.Music => DirectorAudioExtensions.Contains(DirectorAssetExtension(asset)),
        _ => RelevantAssetKinds(kind).Contains(asset.Kind)
    };

    private static readonly HashSet<string> DirectorAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".opus"
    };

    private async Task<DirectorAssetMatch> FindDirectorAssetAsync(
        LoquendoAI.Infrastructure.Persistence.SqliteProjectRepository repository,
        IReadOnlyList<AssetSource> sources, ScriptBlockKind kind, string query, CharacterDefinition? character)
    {
        var requestedPath = query.Trim().Replace('\\', '/').Trim('/');
        var filename = requestedPath.Split('/').Last();
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        var explicitExtension = DirectorImageExtensions.Contains(extension) || DirectorVideoExtensions.Contains(extension) ||
            extension is ".wav" or ".mp3" or ".ogg" or ".flac" or ".m4a" or ".aac" or ".wma" or ".opus";
        var stem = explicitExtension ? filename[..^extension.Length] : filename;
        if (string.IsNullOrWhiteSpace(stem)) return new(null, "Nombre de recurso vacío");

        string Label(AssetRecord item)
        {
            var sourceName = sources.FirstOrDefault(x => x.Id == item.SourceId)?.Name;
            return sourceName is null ? DirectorAssetLabel(item) : sourceName + "/" + DirectorAssetLabel(item);
        }
        DirectorAssetMatch Pick(IReadOnlyList<AssetRecord> matches, bool inferred)
        {
            var chosen = matches[0];
            var note = inferred ? "Recurso sugerido: " + Label(chosen) : "Recurso sugerido";
            if (matches.Count > 1)
            {
                note += " | Otros: " + string.Join("; ", matches.Skip(1).Take(3).Select(Label));
                if (matches.Count > 4) note += $" (+{matches.Count - 4})";
            }
            return new(chosen, Note: note);
        }

        bool Compatible(AssetRecord item) => DirectorAssetCompatible(kind, item) &&
            (item.SourceId is null || sources.Any(source => source.Id == item.SourceId)) &&
            (!explicitExtension || DirectorAssetExtension(item).Equals(extension, StringComparison.OrdinalIgnoreCase));

        if (Guid.TryParse(query.Trim(), out var explicitId))
        {
            var byId = (await repository.GetAssetsByIdsAsync([explicitId])).FirstOrDefault();
            return byId is not null && Compatible(byId) && !byId.IsMissing
                ? Pick([byId], inferred: false)
                : new(null, "ID de recurso no catalogado, faltante o incompatible con este bloque");
        }

        // A typed path wins over fuzzy inference. The same relative path can exist
        // in several sources; the source name disambiguates it when supplied.
        if (requestedPath.Contains('/'))
        {
            var direct = new List<AssetRecord>();
            foreach (var source in sources.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id))
            {
                var candidates = new List<string> { requestedPath };
                if (requestedPath.StartsWith(source.Name + "/", StringComparison.OrdinalIgnoreCase))
                    candidates.Insert(0, requestedPath[(source.Name.Length + 1)..]);
                foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var asset = await repository.GetAssetBySourcePathAsync(source.Id, path);
                    if (asset is not null && Compatible(asset)) direct.Add(asset);
                }
            }
            var paths = direct.DistinctBy(x => x.Id).ToArray();
            if (paths.Length > 0) return Pick(paths, inferred: false);
        }

        IReadOnlyList<AssetFolder> spriteFolders = kind == ScriptBlockKind.CharacterShow && character is not null
            ? await repository.GetSpriteFoldersForCharacterAsync(character.Name) : [];
        var spriteCandidates = new List<AssetRecord>();
        if (spriteFolders.Count > 0)
            foreach (var folder in spriteFolders.Take(20))
            {
                var search = SameDirectorName(stem, character!.Name) ? "" : stem;
                spriteCandidates.AddRange(await repository.SearchAssetsInFolderAsync(
                    RelevantAssetKinds(kind), folder, true, search, 20));
            }
        var foundAssets = await repository.SearchDirectorAssetsAsync(stem);
        bool InCharacterFolder(AssetRecord item) => spriteFolders.Any(folder =>
            item.SourceId == folder.SourceId && item.SourceRelativePath is { } path &&
            (folder.RelativePath.Length == 0 || path.Replace('\\', '/').StartsWith(
                folder.RelativePath.Trim('/') + "/", StringComparison.OrdinalIgnoreCase)));
        // A render found by name alone must not belong to ANOTHER registered character: before
        // 1.0.0-beta.4 «[MOSTRAR] Bart | feliz» took any «feliz» of the library (another
        // character's folder) when Bart had none, or even a prefix match («feli…»).
        bool OwnedByOtherCharacter(AssetRecord item)
        {
            if (kind != ScriptBlockKind.CharacterShow || character is null || InCharacterFolder(item)) return false;
            if (item.SubjectName is { Length: > 0 } subject)
                return !SameDirectorName(subject, character.Name);
            var folders = (item.SourceRelativePath ?? item.RelativePath).Replace('\\', '/').Split('/')[..^1];
            return folders.Any(part => !SameDirectorName(part, character.Name) &&
                _characters.Any(other => SameDirectorName(other.Name, part)));
        }
        bool Belongs(AssetRecord item) => InCharacterFolder(item) ||
            item.SubjectName is { Length: > 0 } subject && SameDirectorName(subject, character!.Name);
        var compatible = foundAssets.Concat(spriteCandidates).Where(Compatible)
            .Where(x => !OwnedByOtherCharacter(x)).DistinctBy(x => x.Id).ToArray();
        AssetRecord[] Rank(IEnumerable<AssetRecord> items) => items
            .OrderByDescending(x => kind == ScriptBlockKind.CharacterShow && InCharacterFolder(x))
            .ThenByDescending(x => RelevantAssetKinds(kind).Contains(x.Kind))
            .ThenBy(x => DirectorNameDistance(stem, x.DisplayName))
            .ThenBy(x => x.DisplayName.Length)
            .ThenBy(Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id).ToArray();

        // "Mostrar Bart" means choose a render from Bart's own folder even if
        // the filename is simply an expression number instead of "Bart".
        if (kind == ScriptBlockKind.CharacterShow && character is not null &&
            SameDirectorName(stem, character.Name))
        {
            var characterRenders = Rank(spriteCandidates.Where(Compatible).DistinctBy(x => x.Id));
            if (characterRenders.Length > 0) return Pick(characterRenders, inferred: true);
        }

        if (kind == ScriptBlockKind.CharacterShow && character is not null)
        {
            // The character's own renders first: exact name, then names containing the words.
            var own = Rank(compatible.Where(Belongs));
            var ownExact = own.Where(x => DirectorAssetNameMatches(stem, explicitExtension ? extension : "", x)).ToArray();
            if (ownExact.Length > 0) return Pick(ownExact, inferred: false);
            if (own.Length > 0) return Pick(own, inferred: true);
            // No render with that name: the most similar render of THIS character, clearly flagged.
            var all = new List<AssetRecord>();
            foreach (var folder in spriteFolders.Take(20))
                all.AddRange(await repository.SearchAssetsInFolderAsync(RelevantAssetKinds(kind), folder, true, "", 151));
            var closest = Rank(all.Where(Compatible).DistinctBy(x => x.Id));
            if (closest.Length > 0)
                return new(closest[0], Note: $"⚠ {character.Name} no tiene un render «{stem}»: se usó {Label(closest[0])}. " +
                    "Cámbialo con «Elegir recurso de fila…» si no es el que querías");
        }

        var exact = Rank(compatible.Where(x => DirectorAssetNameMatches(stem, explicitExtension ? extension : "", x)));
        if (exact.Length > 0) return Pick(exact, inferred: false);

        // A short prefix collects typo candidates without loading the whole catalog.
        // The best compatible match is bound; other matches remain visible in Estado.
        if (compatible.Length == 0 && stem.Length >= 4)
        {
            var nearby = await repository.SearchDirectorAssetsAsync(stem[..Math.Min(4, stem.Length)]);
            compatible = nearby.Concat(spriteCandidates).Where(Compatible)
                .Where(x => !OwnedByOtherCharacter(x)).DistinctBy(x => x.Id).ToArray();
        }
        var suggestions = Rank(compatible);
        if (suggestions.Length > 0) return Pick(suggestions, inferred: true);
        if (kind == ScriptBlockKind.CharacterShow && character is not null && foundAssets.Any(OwnedByOtherCharacter))
            return new(null, $"«{stem}» solo existe en renders de otros personajes; {character.Name} no tiene renders catalogados " +
                "(revisa en Biblioteca la regla de su carpeta)");
        if (foundAssets.Any(x => SameDirectorName(x.DisplayName, stem)))
            return new(null, "Existe en el catálogo, pero su formato no corresponde a este bloque");
        return new(null, "Recurso sin catalogar; revisa nombre, carpeta o escaneo de la fuente");
    }

    private static bool DirectorAssetNameMatches(string stem, string extension, AssetRecord asset)
    {
        return SameDirectorName(stem, asset.DisplayName) &&
            (extension.Length == 0 || DirectorAssetExtension(asset).Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string DirectorAssetLabel(AssetRecord asset) =>
        (asset.SourceRelativePath ?? asset.DisplayName + DirectorAssetExtension(asset)).Replace('\\', '/');

    private static string DirectorKindName(ScriptBlockKind kind) => ScriptBlockTypes.FirstOrDefault(x => x.Kind == kind)?.Name ?? kind.ToString();

    /// <summary>
    /// One row of the draft grids. Rows compare by reference: with record value equality the hash
    /// code changed every time the Instruction cell was edited or a resource was picked, and the
    /// DataGrid (selection, containers and the shared view of both draft grids) then lost track of
    /// the row. That made «Elegir recurso de fila…» sometimes do nothing or write into another row.
    /// Instruction notifies changes so the cell updates without Items.Refresh().
    /// </summary>
    private sealed record DirectorDraftRow(int Order, string Kind, string Subject, string Text, string Parameters,
        string Status, bool IsReady) : System.ComponentModel.INotifyPropertyChanged
    {
        private string? _instruction;

        public string? Instruction
        {
            get => _instruction;
            set
            {
                if (string.Equals(_instruction, value, StringComparison.Ordinal)) return;
                _instruction = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Instruction)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        // «row with { … }» creates a new row: copy the data, never the old row's subscribers.
        private DirectorDraftRow(DirectorDraftRow original)
        {
            Order = original.Order; Kind = original.Kind; Subject = original.Subject; Text = original.Text;
            Parameters = original.Parameters; Status = original.Status; IsReady = original.IsReady;
            _instruction = original._instruction;
        }

        public bool Equals(DirectorDraftRow? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
}

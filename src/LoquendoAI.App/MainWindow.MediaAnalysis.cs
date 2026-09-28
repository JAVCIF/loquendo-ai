using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Library;

namespace LoquendoAI.App;

/// <summary>
/// Library AI analysis (1.3.0).
/// «IA: analizar imágenes seleccionadas» (as before, images and GIF with the vision model) and the new
/// «IA: analizar medios seleccionados»: images with the vision model; music, SFX, videos and GIF by their names with
/// the Director model, many files per request; audios whose name says nothing can then be listened to by Gemini
/// (asked first). Both work on the selection (2 or more rows) or on the whole filter (asked), stop cleanly when the
/// provider runs out of quota and keep everything already analyzed.
/// </summary>
public partial class MainWindow
{
    private const int MaxAnalysisBatch = 500;
    private const int MaxConsecutiveFailures = 3;

    /// <summary>What to analyze: the selection when it has 2+ rows; otherwise the current filter (after asking),
    /// or the single selected row. Selection wins over filter.</summary>
    private AssetRow[]? AnalysisTargets(string what)
    {
        var selected = AssetsGrid.SelectedItems.Cast<AssetRow>().DistinctBy(x => x.AssetId).ToArray();
        if (selected.Length >= 2) return selected;
        var filterActive = !_assetFiltersDirty && _filteredAssetRows.Count > 0 && _filteredAssetRows.Count < _allAssetRows.Count;
        if (filterActive)
        {
            var count = _filteredAssetRows.Count;
            if (selected.Length == 1)
            {
                var answer = MessageBox.Show(this,
                    $"Hay un filtro con {count} recurso(s) y tienes 1 seleccionado.\n\nSí = analizar {what} de los {count} del filtro\nNo = solo el seleccionado",
                    "IA: analizar", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                return answer switch
                {
                    MessageBoxResult.Yes => _filteredAssetRows.ToArray(),
                    MessageBoxResult.No => selected,
                    _ => null
                };
            }
            return MessageBox.Show(this,
                $"No hay nada seleccionado: se analizarán {what} de los {count} recurso(s) del filtro actual (también los que no se ven en la tabla).\n\n¿Continuar?",
                "IA: analizar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes ? _filteredAssetRows.ToArray() : null;
        }
        if (selected.Length == 1) return selected;
        FooterText.Text = "Selecciona recursos o filtra la biblioteca (Buscar) para analizarlos.";
        return null;
    }

    private static string AssetExtension(AssetRecord asset) =>
        Path.GetExtension(asset.SourceRelativePath ?? asset.RelativePath).ToLowerInvariant();

    /// <summary>The director.auto.* tags by asset and field, looked up once (a filter can have thousands of assets).</summary>
    private static Dictionary<(Guid, string), string> AutoTagIndex(IEnumerable<AssetTag> tags) => tags
        .Where(t => t.Key.StartsWith("director.auto.", StringComparison.Ordinal))
        .GroupBy(t => (t.AssetId, t.Key["director.auto.".Length..]))
        .ToDictionary(g => g.Key, g => g.First().Value);

    private static string AutoTag(Dictionary<(Guid, string), string> index, Guid id, string key) =>
        index.TryGetValue((id, key), out var value) ? value : "";

    /// <summary>Described from its image with this model (both buttons: images, and GIF by their first frame).</summary>
    private static bool ImageDone(Dictionary<(Guid, string), string> index, AssetRecord asset, string modelKey) =>
        AutoTag(index, asset.Id, "hash") == asset.Sha256 && AutoTag(index, asset.Id, "version") == DirectorImageAnalyzerVersion &&
        (modelKey.Length == 0 || AutoTag(index, asset.Id, "model") == modelKey);

    /// <summary>Whether the media analysis (by name or by listening) is saved in «version» («medios-1|nombre|baja»,
    /// «medios-1|contenido»): hash, model and version are the tags the library search ignores.</summary>
    private static string MediaVersion(string source, bool? confident = null) =>
        MediaAnalysis.Version + "|" + source + (confident is null ? "" : confident.Value ? "|alta" : "|baja");

    private static string ModelKey(AiModelChoice choice) => choice.Provider == "ollama" ? choice.Model : choice.Provider + ":" + choice.Model;

    /// <summary>Checks that a model can read images before a batch starts.</summary>
    private async Task EnsureVisionAsync(AiModelChoice choice, CancellationToken token)
    {
        if (choice.Provider is "claude" or "openai" && !_aiProvider.Has(choice, "vision"))
            throw new InvalidOperationException($"«{choice.Model}» no admite imágenes según el catálogo de {AiProviderSettings.ProviderName(choice.Provider)}.");
        if (choice.Provider == "ollama" && await OllamaDirectorClient.ModelSupportsVisionAsync(choice.Model, token) == false)
            throw new InvalidOperationException($"«{choice.Model}» no anuncia capacidad de visión en Ollama. Selecciona un modelo que admita imágenes.");
    }

    private Task SaveAutoTagsAsync(AssetRecord asset, Dictionary<string, string> fields, double confidence, CancellationToken token) =>
        _currentRepository!.ReplaceDirectorAssetTagsAsync(asset.Id, "director.auto.",
            fields.Where(p => p.Value.Length > 0).Select(p => new AssetTag(asset.Id, "director.auto." + p.Key,
                p.Value[..Math.Min(500, p.Value.Length)], confidence)), token);

    /// <summary>One image with the vision model (same prompt and tags as always).</summary>
    private async Task AnalyzeImageAsync(AssetRecord asset, string path, AiModelChoice choice, CancellationToken token)
    {
        var image = await Task.Run(() => ImageForAi(path), token);
        var response = await GenerateAiResponseAsync(choice.Provider, choice.Model,
            "Describe únicamente lo que ves. No inventes el personaje por su nombre de archivo. Si la imagen contiene muchos fotogramas o poses pequeñas, clasifícala como hoja_de_sprites y nunca como render individual. Responde en español con el esquema JSON solicitado.",
            "Describe esta imagen para un editor de video. role: fondo/render/prop/gif/hoja_de_sprites; mood: expresión o atmósfera; subject: sujeto o lugar; description: descripción visual breve. Render = una sola pose usable como capa; hoja_de_sprites = múltiples poses o atlas.",
            OllamaDirectorClient.ImageSchema, image, token);
        using var json = JsonDocument.Parse(response);
        string Read(string key) => json.RootElement.TryGetProperty(key, out var value) ? value.GetString()?.Trim() ?? "" : "";
        await SaveAutoTagsAsync(asset, new Dictionary<string, string>
        {
            ["description"] = Read("description"), ["role"] = Read("role"), ["mood"] = Read("mood"), ["subject"] = Read("subject"),
            ["hash"] = asset.Sha256, ["model"] = ModelKey(choice), ["version"] = DirectorImageAnalyzerVersion
        }, 0.75, token);
    }

    private string QuotaMessage(AiQuotaException ex, int done, int total) =>
        $"⚠ La API se quedó sin cuota o crédito: se analizaron {done} de {total}; el resto queda pendiente. {ex.Message} " +
        "Vuelve a pulsar cuando tengas cuota (los ya analizados se omiten).";

    // ───────────────────────────── Images (legacy) ─────────────────────────────

    private async void AnalyzeDirectorAssets_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository || _aiRequestCancellation is not null || _libraryAnalysisRunning) return;
        if (AssetVisionModelBox.SelectedItem is not AiModelChoice choice)
        {
            FooterText.Text = "Selecciona un modelo que admita imágenes y pulsa «Recargar modelos» si hace falta.";
            return;
        }
        if (AnalysisTargets("las imágenes") is not { } rows) return;
        using var cancellation = new CancellationTokenSource();
        _aiRequestCancellation = cancellation;
        AssetCancelAnalysisButton.IsEnabled = AiCancelButton.IsEnabled = true;
        var analyzed = 0;
        var missing = 0;
        var failures = new List<string>();
        var work = new List<AssetRecord>();
        try
        {
            await EnsureVisionAsync(choice, cancellation.Token);
            var ids = rows.Select(x => x.AssetId).ToHashSet();
            var assets = await repository.GetAssetsByIdsAsync(ids);
            var index = AutoTagIndex(await repository.GetAssetTagsAsync());
            var modelKey = ModelKey(choice);
            work = assets.Where(asset => ids.Contains(asset.Id) && !asset.IsMissing &&
                    MediaAnalysis.Classify(AssetExtension(asset), asset.Kind) is MediaCategory.Image or MediaCategory.Gif &&
                    !ImageDone(index, asset, modelKey))
                .ToList();
            var skipped = rows.Length - work.Count;
            if (work.Count == 0)
            {
                FooterText.Text = $"Nada que analizar: {skipped} recurso(s) no son imágenes o ya están analizados con {choice}.";
                return;
            }
            if (work.Count > MaxAnalysisBatch)
            {
                FooterText.Text = $"Son {work.Count} imágenes: el máximo por lote es {MaxAnalysisBatch}. Filtra por carpeta o tipo.";
                return;
            }
            if (work.Count > 20 && MessageBox.Show(this, $"Se analizarán {work.Count} imagen(es) con {choice} ({work.Count} peticiones)." +
                    (skipped > 0 ? $" {skipped} se omiten (no son imágenes o ya están analizadas)." : "") + "\n\n¿Continuar?",
                    "IA: analizar imágenes", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var consecutive = 0;
            for (var i = 0; i < work.Count; i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (_currentRepository != repository) throw new OperationCanceledException();
                var asset = work[i];
                FooterText.Text = $"Analizando {i + 1}/{work.Count}: {asset.DisplayName}…";
                // A file that is not reachable (unplugged drive, moved) is skipped, it is not a failure of the AI.
                var path = await ResolveAssetPathAsync(asset);
                if (path is null || !File.Exists(path)) { missing++; continue; }
                try
                {
                    await AnalyzeImageAsync(asset, path, choice, cancellation.Token);
                    analyzed++;
                    consecutive = 0;
                }
                catch (AiQuotaException) { throw; }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    failures.Add($"{asset.DisplayName}: {ex.Message}");
                    if (++consecutive >= MaxConsecutiveFailures)
                        throw new InvalidOperationException($"{MaxConsecutiveFailures} fallos seguidos; se detuvo el lote. Último: {ex.Message}");
                }
            }
            FooterText.Text = $"Análisis completo: {analyzed} imágenes/GIF nuevas, {skipped} omitidas o ya analizadas" +
                (missing > 0 ? $", {missing} no se encontraron en su carpeta" : "") +
                (failures.Count > 0 ? $", {failures.Count} con error ({failures[0]})." : ".");
        }
        catch (AiQuotaException ex)
        {
            FooterText.Text = QuotaMessage(ex, analyzed, work.Count);
            MessageBox.Show(this, FooterText.Text, "IA: analizar imágenes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (OperationCanceledException) { FooterText.Text = $"Análisis cancelado: {analyzed} etiquetas conservadas."; }
        catch (Exception ex)
        {
            FooterText.Text = ex.Message + (analyzed > 0 ? $" Se conservaron {analyzed} análisis." : "");
            ShowError(ex);
        }
        finally
        {
            await RefreshAfterAnalysisAsync(repository, analyzed);
            _aiRequestCancellation = null;
            AssetCancelAnalysisButton.IsEnabled = AiCancelButton.IsEnabled = false;
        }
    }

    /// <summary>Shows the new descriptions; still inside the analysis, so nothing else starts meanwhile.</summary>
    private async Task RefreshAfterAnalysisAsync(LoquendoAI.Infrastructure.Persistence.SqliteProjectRepository repository, int changed)
    {
        if (changed == 0 || _currentRepository != repository) return;
        try { await RefreshLibraryAsync(); }
        catch (Exception ex) { ErrorLog.Record(ex); }
    }

    // ───────────────────────────── All media ─────────────────────────────

    private sealed record MediaJob(AssetRecord Asset, MediaCategory Category, string FileName, string Folder);

    /// <summary>From «IA: analizar medios» until its optional listening ends, including the library refresh.</summary>
    private bool _libraryAnalysisRunning;

    private async void AnalyzeDirectorMedia_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository || _aiRequestCancellation is not null || _libraryAnalysisRunning) return;
        if (AnalysisTargets("los medios") is not { } rows) return;
        _libraryAnalysisRunning = true;
        try { await AnalyzeMediaAsync(repository, rows); }
        finally { _libraryAnalysisRunning = false; }
    }

    private async Task AnalyzeMediaAsync(LoquendoAI.Infrastructure.Persistence.SqliteProjectRepository repository, AssetRow[] rows)
    {
        var vision = AssetVisionModelBox.SelectedItem as AiModelChoice;
        var text = AiDirectorModelBox.SelectedItem as AiModelChoice;
        using var cancellation = new CancellationTokenSource();
        _aiRequestCancellation = cancellation;
        AssetCancelAnalysisButton.IsEnabled = AiCancelButton.IsEnabled = true;
        int done = 0, total = 0, missing = 0, noVision = 0;
        var failures = new List<string>();
        var listen = new List<MediaJob>();
        try
        {
            var ids = rows.Select(x => x.AssetId).ToHashSet();
            var assets = (await repository.GetAssetsByIdsAsync(ids)).Where(x => ids.Contains(x.Id) && !x.IsMissing).ToArray();
            var index = AutoTagIndex(await repository.GetAssetTagsAsync());
            var images = new List<MediaJob>();
            var names = new List<MediaJob>();
            var already = 0;
            var unsupported = 0;
            foreach (var asset in assets)
            {
                var category = MediaAnalysis.Classify(AssetExtension(asset), asset.Kind);
                var relative = (asset.SourceRelativePath ?? asset.RelativePath).Replace('\\', '/');
                var job = new MediaJob(asset, category, Path.GetFileName(relative),
                    Path.GetFileName(Path.GetDirectoryName(relative.Replace('/', Path.DirectorySeparatorChar)) ?? ""));
                var sameFile = AutoTag(index, asset.Id, "hash") == asset.Sha256;
                var version = AutoTag(index, asset.Id, "version");
                var canListen = category == MediaCategory.Audio && MediaAnalysis.CanListen(AssetExtension(asset));
                switch (category)
                {
                    case MediaCategory.Image:
                        if (vision is null) noVision++;
                        else if (ImageDone(index, asset, ModelKey(vision))) already++;
                        else images.Add(job);
                        break;
                    case MediaCategory.Gif or MediaCategory.Video or MediaCategory.Audio:
                        // Better descriptions are kept: a GIF described from its image, an audio that was listened to.
                        if (ImageDone(index, asset, "") || sameFile && version == MediaVersion("contenido"))
                            already++;
                        // Done by name with this model and the name did not change.
                        else if (sameFile && text is not null && version.StartsWith(MediaVersion("nombre"), StringComparison.Ordinal) &&
                                 AutoTag(index, asset.Id, "model") == ModelKey(text) && AutoTag(index, asset.Id, "name") == job.FileName)
                        {
                            already++;
                            // Its name did not say enough: it can still be listened to (e.g. once Gemini is connected).
                            if (canListen && version.EndsWith("|baja", StringComparison.Ordinal)) listen.Add(job);
                        }
                        // A name that says nothing: no tokens spent on it; an audio can still be listened to.
                        else if (MediaAnalysis.LooksGeneric(job.FileName))
                        {
                            if (canListen) listen.Add(job);
                            else unsupported++;
                        }
                        else names.Add(job);
                        break;
                    default:
                        unsupported++;
                        break;
                }
            }
            if (names.Count > 0 && text is null)
                throw new InvalidOperationException("Para describir por nombre hace falta un modelo de texto: elige el del Director IA (pestaña Guion → Director IA).");
            total = images.Count + names.Count;
            if (total > MaxAnalysisBatch)
                throw new InvalidOperationException($"Son {total} recursos: el máximo por lote es {MaxAnalysisBatch}. Filtra por carpeta o tipo.");
            if (total == 0 && listen.Count == 0)
            {
                FooterText.Text = $"Nada que analizar: {already} ya analizados, {unsupported} sin datos útiles o de un tipo que no se analiza" +
                    (noVision > 0 ? $", {noVision} imágenes sin modelo de visión (elige uno en «Visión»)." : ".");
                return;
            }
            var batches = (int)Math.Ceiling(names.Count / (double)MediaAnalysis.NameBatchSize);
            if (total > 0 && (images.Count > 10 || names.Count > 50) && MessageBox.Show(this,
                    "Se analizarán:\n" +
                    (images.Count > 0 ? $"• {images.Count} imagen(es) con {vision} ({images.Count} peticiones)\n" : "") +
                    (names.Count > 0 ? $"• {names.Count} audio/video/GIF por su nombre con {text} ({batches} peticiones de hasta {MediaAnalysis.NameBatchSize})\n" : "") +
                    (already > 0 ? $"Se omiten {already} ya analizados.\n" : "") + "\n¿Continuar?",
                    "IA: analizar medios", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            if (images.Count > 0) await EnsureVisionAsync(vision!, cancellation.Token);

            // 1. By name, in batches: cheap and fast.
            var consecutive = 0;
            for (var b = 0; b < batches; b++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (_currentRepository != repository) throw new OperationCanceledException();
                var batch = names.Skip(b * MediaAnalysis.NameBatchSize).Take(MediaAnalysis.NameBatchSize).ToArray();
                var items = batch.Select((job, i) => new MediaNameItem($"A{i + 1}", job.Category, job.Asset.Kind, job.FileName, job.Folder)).ToArray();
                FooterText.Text = $"Por nombre: lote {b + 1}/{batches} ({batch.Length} archivos) con {text}…";
                try
                {
                    var output = await GenerateAiResponseAsync(text!.Provider, text.Model, MediaAnalysis.NameSystem,
                        MediaAnalysis.NamePrompt(items), MediaAnalysis.NameSchema(items.Select(x => x.Ref)), null, cancellation.Token,
                        new Progress<string>(message => FooterText.Text = message), new OllamaChatOptions(Math.Max(1024, batch.Length * 110), "medios-nombre"));
                    var answer = MediaAnalysis.ParseNameAnswer(output);
                    for (var i = 0; i < batch.Length; i++)
                    {
                        var job = batch[i];
                        if (!answer.TryGetValue(items[i].Ref, out var described) || described.Description.Length == 0)
                        {
                            if (job.Category == MediaCategory.Audio && MediaAnalysis.CanListen(AssetExtension(job.Asset))) listen.Add(job);
                            continue;
                        }
                        await SaveAutoTagsAsync(job.Asset, new Dictionary<string, string>
                        {
                            ["description"] = described.Description, ["role"] = described.Role, ["mood"] = described.Mood,
                            ["subject"] = described.Subject, ["hash"] = job.Asset.Sha256, ["model"] = ModelKey(text),
                            ["version"] = MediaVersion("nombre", described.Confident), ["name"] = job.FileName
                        }, described.Confident ? 0.6 : 0.3, cancellation.Token);
                        done++;
                        if (!described.Confident && job.Category == MediaCategory.Audio && MediaAnalysis.CanListen(AssetExtension(job.Asset)))
                            listen.Add(job);
                    }
                    consecutive = 0;
                }
                catch (AiQuotaException) { throw; }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    failures.Add($"lote {b + 1}: {ex.Message}");
                    if (++consecutive >= MaxConsecutiveFailures)
                        throw new InvalidOperationException($"{MaxConsecutiveFailures} fallos seguidos; se detuvo el lote. Último: {ex.Message}");
                }
            }

            // 2. Images, one per request with the vision model.
            consecutive = 0;
            for (var i = 0; i < images.Count; i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (_currentRepository != repository) throw new OperationCanceledException();
                var job = images[i];
                FooterText.Text = $"Imágenes: {i + 1}/{images.Count} {job.FileName} con {vision}…";
                var path = await ResolveAssetPathAsync(job.Asset);
                if (path is null || !File.Exists(path)) { missing++; continue; }
                try
                {
                    await AnalyzeImageAsync(job.Asset, path, vision!, cancellation.Token);
                    done++;
                    consecutive = 0;
                }
                catch (AiQuotaException) { throw; }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    failures.Add($"{job.FileName}: {ex.Message}");
                    if (++consecutive >= MaxConsecutiveFailures)
                        throw new InvalidOperationException($"{MaxConsecutiveFailures} fallos seguidos; se detuvo el lote. Último: {ex.Message}");
                }
            }
            FooterText.Text = $"Análisis de medios: {done} de {total} descritos" + (already > 0 ? $", {already} ya estaban" : "") +
                (missing > 0 ? $", {missing} no se encontraron en su carpeta" : "") +
                (noVision > 0 ? $", {noVision} imágenes sin modelo de visión" : "") +
                (failures.Count > 0 ? $", {failures.Count} con error ({failures[0]})" : "") + ".";
        }
        catch (AiQuotaException ex)
        {
            FooterText.Text = QuotaMessage(ex, done, total);
            MessageBox.Show(this, FooterText.Text, "IA: analizar medios", MessageBoxButton.OK, MessageBoxImage.Warning);
            // Listening uses Gemini: still possible when the exhausted provider was another one.
            if (text?.Provider == "gemini" || vision?.Provider == "gemini") listen.Clear();
        }
        catch (OperationCanceledException)
        {
            FooterText.Text = $"Análisis cancelado: {done} descripciones conservadas.";
            listen.Clear();
        }
        catch (Exception ex)
        {
            FooterText.Text = ex.Message + (done > 0 ? $" Se conservaron {done} análisis." : "");
            ShowError(ex);
            listen.Clear();
        }
        finally
        {
            await RefreshAfterAnalysisAsync(repository, done);
            _aiRequestCancellation = null;
            AssetCancelAnalysisButton.IsEnabled = AiCancelButton.IsEnabled = false;
        }
        if (listen.Count > 0 && _currentRepository == repository) await OfferListeningAsync(repository, listen.DistinctBy(x => x.Asset.Id).ToArray());
    }

    /// <summary>The first visible Gemini model (Flash preferred: cheap and fast); Gemini understands audio.</summary>
    private AiModelChoice? AudioModel() => _aiProvider.VisibleModels()
        .Where(x => x.Provider == "gemini" && !x.Model.Contains("tts", StringComparison.OrdinalIgnoreCase) &&
                    !x.Model.Contains("image", StringComparison.OrdinalIgnoreCase) && !x.Model.Contains("live", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(x => x.Model.Contains("flash", StringComparison.OrdinalIgnoreCase))
        .ThenBy(x => x.Model.Contains("lite", StringComparison.OrdinalIgnoreCase))
        .ThenByDescending(x => x.Model, StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>Audios the name could not describe: listening to them needs a model that hears audio (Gemini),
    /// so it is offered, not done silently.</summary>
    private async Task OfferListeningAsync(LoquendoAI.Infrastructure.Persistence.SqliteProjectRepository repository, MediaJob[] jobs)
    {
        var model = AiProviderSettings.HasGeminiKey ? AudioModel() : null;
        var examples = string.Join(", ", jobs.Take(5).Select(x => x.FileName)) + (jobs.Length > 5 ? "…" : "");
        if (model is null)
        {
            MessageBox.Show(this, $"{jobs.Length} audio(s) no se pudieron describir bien por su nombre ({examples}). Lo que se logró quedó guardado.\n\n" +
                "Para describirlos por lo que suenan hace falta un modelo que escuche audio: Gemini (Google; su API tiene nivel gratuito). " +
                "Conéctalo en «Configurar IA» y vuelve a pulsar «IA: analizar medios seleccionados». No hace falta ElevenLabs.",
                "IA: analizar medios", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"{jobs.Length} audio(s) no se pudieron describir bien por su nombre ({examples}). Lo que se logró quedó guardado " +
                "y puedes conservarlo.\n\n¿Escucharlos con " + model + $"? Se envían hasta {MediaAnalysis.AudioClipSeconds} s de cada uno " +
                $"({jobs.Length} peticiones; necesita FFmpeg).", "IA: analizar medios · escuchar audio",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            FooterText.Text = "Se conservaron las descripciones por nombre; los audios no se escucharon.";
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _aiRequestCancellation = cancellation;
        AssetCancelAnalysisButton.IsEnabled = AiCancelButton.IsEnabled = true;
        var done = 0;
        var failures = new List<string>();
        try
        {
            var consecutive = 0;
            for (var i = 0; i < jobs.Length; i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (_currentRepository != repository) throw new OperationCanceledException();
                var job = jobs[i];
                FooterText.Text = $"Escuchando {i + 1}/{jobs.Length}: {job.FileName} con {model}…";
                var path = await ResolveAssetPathAsync(job.Asset);
                if (path is null || !File.Exists(path)) continue;
                try
                {
                    var clip = await AudioClipAsync(path, cancellation.Token);
                    var output = await GeminiDirectorClient.ChatAsync(model.Model, MediaAnalysis.ListenSystem,
                        MediaAnalysis.ListenPrompt(job.FileName, job.Asset.Kind), OllamaDirectorClient.ImageSchema, clip,
                        _aiProvider.ThinkingLevel, cancellation.Token, new Progress<string>(message => FooterText.Text = message),
                        "medios-audio", "audio/mp3");
                    using var json = JsonDocument.Parse(output);
                    string Read(string key) => json.RootElement.TryGetProperty(key, out var value) ? value.GetString()?.Trim() ?? "" : "";
                    await SaveAutoTagsAsync(job.Asset, new Dictionary<string, string>
                    {
                        ["description"] = Read("description"), ["role"] = Read("role"), ["mood"] = Read("mood"), ["subject"] = Read("subject"),
                        ["hash"] = job.Asset.Sha256, ["model"] = ModelKey(model), ["version"] = MediaVersion("contenido"), ["name"] = job.FileName
                    }, 0.75, cancellation.Token);
                    done++;
                    consecutive = 0;
                }
                catch (AiQuotaException) { throw; }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    failures.Add($"{job.FileName}: {ex.Message}");
                    if (++consecutive >= MaxConsecutiveFailures)
                        throw new InvalidOperationException($"{MaxConsecutiveFailures} fallos seguidos; se detuvo. Último: {ex.Message}");
                }
            }
            FooterText.Text = $"Audios escuchados: {done} de {jobs.Length} descritos por su contenido" +
                (failures.Count > 0 ? $", {failures.Count} con error ({failures[0]})." : ".");
        }
        catch (AiQuotaException ex)
        {
            FooterText.Text = QuotaMessage(ex, done, jobs.Length);
            MessageBox.Show(this, FooterText.Text, "IA: escuchar audio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (OperationCanceledException) { FooterText.Text = $"Escucha cancelada: {done} descripciones conservadas."; }
        catch (Exception ex)
        {
            FooterText.Text = ex.Message + (done > 0 ? $" Se conservaron {done}." : "");
            ShowError(ex);
        }
        finally
        {
            await RefreshAfterAnalysisAsync(repository, done);
            _aiRequestCancellation = null;
            AssetCancelAnalysisButton.IsEnabled = AiCancelButton.IsEnabled = false;
        }
    }

    /// <summary>The first seconds of an audio as a small mono MP3 (base64): few tokens and fast to send.</summary>
    private static async Task<string> AudioClipAsync(string path, CancellationToken token)
    {
        var output = Path.Combine(Path.GetTempPath(), $"loquendoai-clip-{Guid.NewGuid():N}.mp3");
        try
        {
            var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true, UseShellExecute = false };
            foreach (var argument in new[] { "-v", "error", "-y", "-i", path, "-t", MediaAnalysis.AudioClipSeconds.ToString(), "-vn", "-ac", "1",
                         "-ar", "16000", "-b:a", "32k", output })
                start.ArgumentList.Add(argument);
            Process process;
            try { process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg."); }
            catch (System.ComponentModel.Win32Exception ex) { throw new InvalidOperationException("No se encontró FFmpeg (hace falta para enviar el audio).", ex); }
            using (process)
            {
                var errors = process.StandardError.ReadToEndAsync(token);
                try { await process.WaitForExitAsync(token); }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch (InvalidOperationException) { } // already finished
                    throw;
                }
                if (process.ExitCode != 0 || !File.Exists(output))
                    throw new InvalidOperationException("FFmpeg no pudo leer el audio: " + (await errors).Trim());
            }
            return Convert.ToBase64String(await File.ReadAllBytesAsync(output, token));
        }
        finally
        {
            try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { }
        }
    }
}

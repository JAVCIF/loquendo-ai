using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Tts;

namespace LoquendoAI.App;

public partial class MainWindow
{
    /// <summary>
    /// Parallel TTS jobs for "Generar voces". Each line still runs in its own isolated bridge
    /// process (the Loquendo engine is never shared), so parallelism only overlaps process start-up
    /// and synthesis. LOQUENDO_AI_TTS_PARALLEL=1 restores the old one-by-one behaviour.
    /// </summary>
    internal static int TtsParallelism =>
        int.TryParse(Environment.GetEnvironmentVariable("LOQUENDO_AI_TTS_PARALLEL"), out var value)
            ? Math.Clamp(value, 1, 6) : 2;

    /// <summary>Stops the parallel phase after this many failed lines; the one-by-one loop then
    /// retries the remaining lines and reports errors exactly as before.</summary>
    private const int VoicePrefetchFailureLimit = 2;

    internal sealed record VoicePrefetchResult(IReadOnlySet<string> Generated, int Failed, long ElapsedMs, int Parallelism);

    /// <summary>
    /// Synthesizes the missing lines of a scene into the voice cache before the ordered pass in
    /// GenerateSceneVoicesAsync. That pass is unchanged: it finds each line in the cache and copies
    /// it to its scene file, and it still synthesizes (and reports) any line this phase could not.
    /// </summary>
    private async Task<VoicePrefetchResult> PrefetchSceneVoicesAsync(string projectRoot, IReadOnlyList<SceneScriptBlock> ordered,
        IReadOnlyDictionary<Guid, CharacterDefinition> characterMap, IReadOnlyDictionary<Guid, VoiceProfile> profileMap,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        var parallelism = TtsParallelism;
        var empty = new VoicePrefetchResult(new HashSet<string>(), 0, 0, parallelism);
        if (parallelism < 2) return empty;
        var jobs = new Dictionary<string, TtsPreviewRequest>(StringComparer.Ordinal);
        var staging = Path.Combine(VoiceCache.Folder(projectRoot), "_staging");
        foreach (var block in ordered)
        {
            if (!IsSpeechBlock(block.Kind) || IsImportedVoice(block) || string.IsNullOrWhiteSpace(block.Text)) continue;
            var profile = ResolveVoiceProfile(block, characterMap, profileMap);
            if (profile is null) continue;
            var pitch = block.VoicePitchOverride ?? profile.Pitch;
            var speed = block.VoiceSpeedOverride ?? profile.Speed;
            var volume = block.VoiceVolumeOverride ?? profile.Volume;
            var hash = ComputeVoiceCacheHash(profile, block.Text, pitch, speed, volume);
            if (jobs.ContainsKey(hash)) continue;
            var existing = ResolveGeneratedPath(block.GeneratedAudioPath);
            if (block.GeneratedAudioHash == hash && existing is not null && IsValidWave(existing)) continue;
            if (VoiceCache.Find(projectRoot, hash, IsValidWave) is not null) continue;
            jobs[hash] = new TtsPreviewRequest(profile.ProviderKey, profile.VoiceId, block.Text,
                Path.Combine(staging, hash + "." + Guid.NewGuid().ToString("N")[..8] + ".wav"),
                pitch, speed, volume, profile.SampleRate);
        }
        // One line gains nothing from the parallel phase; the ordered pass handles it.
        if (jobs.Count < 2) return empty;

        var clock = Stopwatch.StartNew();
        var generated = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var failed = 0;
        var done = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        progress.Report($"Generando {jobs.Count} voces ({parallelism} a la vez)…");
        try
        {
            await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = stop.Token },
                async (job, token) =>
                {
                    var output = job.Value.OutputPath;
                    try
                    {
                        await _ttsBridge.SynthesizeAsync(job.Value, token).ConfigureAwait(false);
                        if (IsValidWave(output))
                        {
                            VoiceCache.Store(projectRoot, job.Key, output);
                            if (IsValidWave(VoiceCache.PathFor(projectRoot, job.Key))) generated.TryAdd(job.Key, 0);
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        AiDiagnostics.Note($"tts: fallo en paralelo ({job.Value.ProviderKey}/{job.Value.VoiceId}, {job.Value.Text.Length} car): " +
                            ex.GetType().Name + ": " + ex.Message.Replace('\r', ' ').Replace('\n', ' '));
                        if (Interlocked.Increment(ref failed) >= VoicePrefetchFailureLimit) stop.Cancel();
                    }
                    finally
                    {
                        try { if (File.Exists(output)) File.Delete(output); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                        progress.Report($"Generando voces: {Interlocked.Increment(ref done)}/{jobs.Count} ({parallelism} a la vez)…");
                    }
                }).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Stopped after repeated failures: the ordered pass takes over.
        }
        AiDiagnostics.Note($"tts: fase paralela {generated.Count}/{jobs.Count} voces en {clock.ElapsedMilliseconds:N0} ms " +
            $"({parallelism} a la vez, {failed} fallos{(failed >= VoicePrefetchFailureLimit ? ", se continuó una por una" : "")})");
        return new VoicePrefetchResult(generated.Keys.ToHashSet(StringComparer.Ordinal), failed, clock.ElapsedMilliseconds, parallelism);
    }
}

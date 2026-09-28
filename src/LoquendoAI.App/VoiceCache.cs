using System.IO;

namespace LoquendoAI.App;

/// <summary>
/// Content-addressed store for synthesized lines. The voice hash already covers provider,
/// voice, sample rate, prosody and text, so any WAV with the same hash is interchangeable.
/// Without this, every AI redraft (new block IDs, no GeneratedAudioHash) re-synthesized
/// lines that were already on disk.
/// </summary>
internal static class VoiceCache
{
    public static string Folder(string projectRoot) =>
        Path.Combine(projectRoot, "generated", "voices", "_cache");

    public static string PathFor(string projectRoot, string hash) =>
        Path.Combine(Folder(projectRoot), hash + ".wav");

    /// <summary>Returns a valid WAV for this hash: first the cache, then any older scene file
    /// named "..._{hash[..10]}.wav" produced before the cache existed.</summary>
    public static string? Find(string projectRoot, string hash, Func<string, bool> isValidWave)
    {
        if (hash.Length < 10) return null;
        var cached = PathFor(projectRoot, hash);
        if (File.Exists(cached) && isValidWave(cached)) return cached;

        var voices = Path.Combine(projectRoot, "generated", "voices");
        if (!Directory.Exists(voices)) return null;
        try
        {
            foreach (var candidate in Directory.EnumerateFiles(voices, "*_" + hash[..10] + ".wav", SearchOption.AllDirectories))
            {
                if (!isValidWave(candidate)) continue;
                Store(projectRoot, hash, candidate);
                return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Best effort: a cache failure must never fail voice generation.</summary>
    public static void Store(string projectRoot, string hash, string wavPath)
    {
        try
        {
            var target = PathFor(projectRoot, hash);
            if (File.Exists(target) || !File.Exists(wavPath)) return;
            Directory.CreateDirectory(Folder(projectRoot));
            var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.Copy(wavPath, temp, true);
            File.Move(temp, target, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

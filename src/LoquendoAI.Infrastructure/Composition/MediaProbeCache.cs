using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>
/// Remembers what ffprobe/ffmpeg measured for a file (duration, audio stream, dimensions, alpha,
/// visible bounds, baked borders) so previews, VEGAS exports and voice generation stop launching
/// the same probes for unchanged files. Entries are keyed by full path + size + last write time,
/// so an edited or replaced file is measured again. Stored in
/// %LOCALAPPDATA%\LoquendoAI\probe-cache.json; LOQUENDO_AI_PROBE_CACHE=0 disables it.
/// </summary>
public static class MediaProbeCache
{
    private const int MaxEntries = 30_000;
    private static readonly object Gate = new();
    private static ConcurrentDictionary<string, Entry>? _entries;
    private static int _saveScheduled;
    private static int _hits;
    private static int _misses;

    private sealed record Entry(string V, long T);

    public static string PathName => AppDataFolder.PathOf("probe-cache.json");

    public static bool Enabled => Environment.GetEnvironmentVariable("LOQUENDO_AI_PROBE_CACHE") is not ("0" or "false" or "no");

    public static (int Hits, int Misses, int Entries) Stats => (_hits, _misses, _entries?.Count ?? 0);

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static ConcurrentDictionary<string, Entry> Entries
    {
        get
        {
            if (_entries is { } ready) return ready;
            lock (Gate)
            {
                if (_entries is not null) return _entries;
                var loaded = new ConcurrentDictionary<string, Entry>(PathComparer);
                try
                {
                    if (File.Exists(PathName) &&
                        JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(PathName)) is { } stored)
                        foreach (var (key, value) in stored)
                            if (value?.V is not null) loaded[key] = value;
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* Start empty. */ }
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Save();
                return _entries = loaded;
            }
        }
    }

    /// <summary>"kind|fullpath|size|mtime", or null when the file cannot be read.</summary>
    private static string? Key(string kind, string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            return string.Join('|', kind, info.FullName, info.Length.ToString(CultureInfo.InvariantCulture),
                info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static bool TryGet(string kind, string path, out string value)
    {
        value = "";
        if (!Enabled || Key(kind, path) is not { } key || !Entries.TryGetValue(key, out var entry))
        {
            Interlocked.Increment(ref _misses);
            return false;
        }
        Interlocked.Increment(ref _hits);
        value = entry.V;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - entry.T > 86_400)
            Entries[key] = entry with { T = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        return true;
    }

    public static void Set(string kind, string path, string value)
    {
        if (!Enabled || Key(kind, path) is not { } key) return;
        Entries[key] = new Entry(value, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (Interlocked.Exchange(ref _saveScheduled, 1) == 0)
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000).ConfigureAwait(false);
                Interlocked.Exchange(ref _saveScheduled, 0);
                Save();
            });
    }

    /// <summary>Returns the cached value or runs <paramref name="probe"/>; a probe that throws or
    /// returns null is not cached (the next call measures again).</summary>
    public static async Task<string> GetOrAddAsync(string kind, string path, Func<Task<string?>> probe)
    {
        if (TryGet(kind, path, out var cached)) return cached;
        var measured = await probe().ConfigureAwait(false);
        if (measured is null) return "";
        Set(kind, path, measured);
        return measured;
    }

    public static void Save()
    {
        if (_entries is not { } entries) return;
        lock (Gate)
        {
            try
            {
                var snapshot = entries.ToArray();
                if (snapshot.Length > MaxEntries)
                {
                    snapshot = snapshot.OrderByDescending(x => x.Value.T).Take(MaxEntries).ToArray();
                    var keep = snapshot.Select(x => x.Key).ToHashSet(PathComparer);
                    foreach (var key in entries.Keys.Where(x => !keep.Contains(x)).ToArray()) entries.TryRemove(key, out _);
                }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
                var temp = PathName + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(snapshot.ToDictionary(x => x.Key, x => x.Value)), new UTF8Encoding(false));
                File.Move(temp, PathName, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Best effort. */ }
        }
    }

    /// <summary>Drops entries whose file no longer exists or changed. Returns how many were removed.</summary>
    public static int PruneStale()
    {
        var entries = Entries;
        var removed = 0;
        foreach (var key in entries.Keys.ToArray())
        {
            var parts = key.Split('|');
            if (parts.Length < 4) { removed += entries.TryRemove(key, out _) ? 1 : 0; continue; }
            var path = string.Join('|', parts[1..^2]);
            if (Key(parts[0], path) != key && entries.TryRemove(key, out _)) removed++;
        }
        if (removed > 0) Save();
        return removed;
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _entries?.Clear();
            try { if (File.Exists(PathName)) File.Delete(PathName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Duration of a PCM/float WAV from its header, without starting ffprobe: data bytes / byte
    /// rate, the same figure ffprobe reports for these files. Null for compressed or unusual WAVs.
    /// </summary>
    public static long? WaveDurationMs(string path)
    {
        if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream, Encoding.ASCII);
            if (stream.Length < 44 || new string(reader.ReadChars(4)) != "RIFF") return null;
            reader.ReadUInt32();
            if (new string(reader.ReadChars(4)) != "WAVE") return null;
            ushort format = 0;
            uint byteRate = 0;
            while (stream.Position + 8 <= stream.Length)
            {
                var id = new string(reader.ReadChars(4));
                var size = reader.ReadUInt32();
                var next = stream.Position + size + (size & 1);
                if (id == "fmt " && size >= 16)
                {
                    format = reader.ReadUInt16();
                    reader.ReadUInt16(); // channels
                    reader.ReadUInt32(); // sample rate
                    byteRate = reader.ReadUInt32();
                }
                else if (id == "data")
                {
                    // 1 = PCM, 3 = IEEE float, 0xFFFE = extensible (PCM/float in practice).
                    if (format is not (1 or 3 or 0xFFFE) || byteRate == 0) return null;
                    // Streams written while recording may leave the size at 0 or 0xFFFFFFFF.
                    var available = stream.Length - stream.Position;
                    var bytes = size == 0 || size == uint.MaxValue || size > available ? available : size;
                    if (bytes <= 0) return null;
                    return Math.Max(1, (long)Math.Ceiling(bytes * 1000d / byteRate));
                }
                if (next > stream.Length) return null;
                stream.Position = next;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException) { }
        return null;
    }
}

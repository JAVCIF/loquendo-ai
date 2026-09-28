using System.IO;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Director;

/// <summary>
/// Duration bounds from file size alone (no disk access), used to keep music and sound effects
/// apart for the AI when the library has not classified a sound. Compressed audio is bounded by
/// 64–320 kbps; WAV by 16-bit mono 22.05 kHz (44 100 B/s) to 16-bit stereo 48 kHz (192 000 B/s).
/// </summary>
public static class AudioDurationEstimate
{
    public const double EffectMaxSeconds = 10;
    public const double MusicMinSeconds = 60;

    public static (double Min, double Max, double Typical) Seconds(AssetRecord asset)
    {
        var size = Math.Max(0, asset.FileSize);
        var extension = (asset.Extension.Length > 0 ? asset.Extension : Path.GetExtension(asset.SourceRelativePath ?? asset.RelativePath))
            .ToLowerInvariant();
        return extension == ".wav"
            ? (size / 192_000d, size / 44_100d, size / 176_400d)
            : (size / 40_000d, size / 8_000d, size / 20_000d);
    }

    /// <summary>"musica", "sfx" or null when the length does not decide it.</summary>
    public static string? ClassifyUnlabelled(AssetRecord asset)
    {
        var (min, max, _) = Seconds(asset);
        if (asset.FileSize <= 0) return null;
        if (max <= EffectMaxSeconds) return "sfx";
        if (min >= MusicMinSeconds) return "musica";
        return null;
    }

    /// <summary>A catalogued SFX that is clearly minutes long, or "music" that lasts a couple of
    /// seconds, is kept away from the AI for that use.</summary>
    public static bool TooLongForEffect(AssetRecord asset) => asset.FileSize > 0 && Seconds(asset).Min >= 45;
    public static bool TooShortForMusic(AssetRecord asset) => asset.FileSize > 0 && Seconds(asset).Max <= 5;

    public static string Label(double seconds) => seconds < 60
        ? $"{Math.Max(1, Math.Round(seconds)):0}s"
        : $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";
}

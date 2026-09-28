using System.Diagnostics;
using System.Globalization;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>Conservatively removes symmetrical flat strips baked into background images.</summary>
public static class BackgroundBorders
{
    private const int Width = 256;
    private const int Height = 144;

    public static async Task<string> CropAsync(string path, CancellationToken token)
    {
        // "crop1": bump the suffix if the detection below changes, so old results are not reused.
        if (MediaProbeCache.TryGet("crop1", path, out var cached)) return cached;
        var (crop, measured) = await CropUncachedAsync(path, token);
        if (measured) MediaProbeCache.Set("crop1", path, crop);
        return crop;
    }

    private static async Task<(string Crop, bool Measured)> CropUncachedAsync(string path, CancellationToken token)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true,
            RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-i", path, "-vf",
            $"scale={Width}:{Height},format=rgb24", "-frames:v", "1", "-f", "rawvideo",
            "-pix_fmt", "rgb24", "pipe:1" }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg para medir el fondo.");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, token);
        var error = process.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(copy, error, process.WaitForExitAsync(token));
        var pixels = output.ToArray();
        if (process.ExitCode != 0 || pixels.Length != Width * Height * 3) return ("", false);

        double Variation(bool columns, int index)
        {
            var first = columns ? Height / 10 : Width / 10;
            var last = columns ? Height * 9 / 10 : Width * 9 / 10;
            double sum = 0, square = 0;
            var count = 0;
            for (var i = first; i < last; i += 2)
            {
                var offset = ((columns ? i : index) * Width + (columns ? index : i)) * 3;
                var light = (pixels[offset] * 30 + pixels[offset + 1] * 59 + pixels[offset + 2] * 11) / 100d;
                sum += light; square += light * light; count++;
            }
            return Math.Sqrt(Math.Max(0, square / count - (sum / count) * (sum / count)));
        }

        int Edge(bool columns, bool reverse)
        {
            var length = columns ? Width : Height;
            var max = (int)(length * .14);
            for (var i = 0; i < max; i++)
                if (Variation(columns, reverse ? length - 1 - i : i) > 15) return i;
            return 0; // Flat scenery is not proof of a baked border.
        }

        var left = Edge(true, false);
        var right = Edge(true, true);
        var top = Edge(false, false);
        var bottom = Edge(false, true);
        if (left < 3 || right < 3) left = right = 0;
        if (top < 2 || bottom < 2) top = bottom = 0;
        if (left + right + top + bottom == 0) return ("", true);
        var fmt = CultureInfo.InvariantCulture;
        string Fraction(int value, int total) => (value / (double)total).ToString("0.######", fmt);
        return ($"crop=iw*{Fraction(Width - left - right, Width)}:ih*{Fraction(Height - top - bottom, Height)}:" +
            $"iw*{Fraction(left, Width)}:ih*{Fraction(top, Height)},", true);
    }
}

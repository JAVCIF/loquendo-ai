using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace LoquendoAI.Tests;

/// <summary>A temporary folder deleted at the end of the test.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "loquendo-tests-" + Guid.NewGuid().ToString("N")[..10]);

    public TempFolder() => Directory.CreateDirectory(Path);

    public string File(string relative) => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose()
    {
        // Pooled SQLite connections keep project.db open after Dispose (Windows would refuse the delete).
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); return; }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }
}

/// <summary>Media written by the tests themselves, so the repository needs no binary fixtures.</summary>
internal static class TestMedia
{
    /// <summary>RGBA PNG of width×height; <paramref name="pixel"/> gives the colour of each pixel.</summary>
    public static string Png(string path, int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var raw = new byte[height * (width * 4 + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 4 + 1);
            raw[row] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var i = row + 1 + x * 4;
                raw[i] = r; raw[i + 1] = g; raw[i + 2] = b; raw[i + 3] = a;
            }
        }
        using var file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6; // 8-bit RGBA
        Chunk(file, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(raw);
            Chunk(file, "IDAT", compressed.ToArray());
        }
        Chunk(file, "IEND", []);
        return path;
    }

    public static string SolidPng(string path, int width, int height, (byte R, byte G, byte B) color) =>
        Png(path, width, height, (_, _) => (color.R, color.G, color.B, 255));

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(typed);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>PCM WAV (a quiet tone) of the given length.</summary>
    public static string Wav(string path, int milliseconds, int sampleRate = 22050, int channels = 1, int bits = 16, bool floatSamples = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytesPerSample = bits / 8;
        var frames = (int)((long)sampleRate * milliseconds / 1000);
        var dataBytes = frames * channels * bytesPerSample;
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + dataBytes); writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt ")); writer.Write(16); writer.Write((short)(floatSamples ? 3 : 1));
        writer.Write((short)channels); writer.Write(sampleRate); writer.Write(sampleRate * channels * bytesPerSample);
        writer.Write((short)(channels * bytesPerSample)); writer.Write((short)bits);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataBytes);
        for (var i = 0; i < frames; i++)
        {
            var value = Math.Sin(i * 2 * Math.PI * 440 / sampleRate) * 0.2;
            for (var c = 0; c < channels; c++)
                if (floatSamples) writer.Write((float)value);
                else if (bits == 16) writer.Write((short)(value * short.MaxValue));
                else if (bits == 24) { var v = (int)(value * 8_388_607); writer.Write((byte)v); writer.Write((byte)(v >> 8)); writer.Write((byte)(v >> 16)); }
                else writer.Write((byte)(128 + value * 127));
        }
        return path;
    }

    private static bool? _ffmpeg;

    /// <summary>Skips the test when ffmpeg/ffprobe are not in the PATH.</summary>
    public static void RequireFfmpeg()
    {
        _ffmpeg ??= Works("ffmpeg") && Works("ffprobe");
        if (_ffmpeg != true) throw new SkipException("FFmpeg/ffprobe no están en el PATH");

        static bool Works(string tool)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(tool, "-version")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
                if (process is null) return false;
                process.StandardOutput.ReadToEnd();
                process.WaitForExit(10_000);
                return process.ExitCode == 0;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
        }
    }

    /// <summary>RGBA pixels of one frame of an image or video (via FFmpeg).</summary>
    public static (int Width, int Height, byte[] Rgba) Frame(string path, double atSeconds = 0)
    {
        var size = Run("ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0:s=x", path)
            .Trim().Split('x');
        var width = int.Parse(size[0]);
        var height = int.Parse(size[1]);
        var info = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-v", "error" }) info.ArgumentList.Add(a);
        if (atSeconds > 0) { info.ArgumentList.Add("-ss"); info.ArgumentList.Add(atSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        foreach (var a in new[] { "-i", path, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgba", "-" }) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        using var memory = new MemoryStream();
        var error = process.StandardError.ReadToEndAsync();
        process.StandardOutput.BaseStream.CopyTo(memory);
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("ffmpeg: " + error.Result);
        return (width, height, memory.ToArray());
    }

    /// <summary>Bounding box [left, top, right, bottom) of the opaque pixels matching <paramref name="match"/>.</summary>
    public static int[]? BoundingBox((int Width, int Height, byte[] Rgba) frame, Func<byte, byte, byte, bool> match)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < frame.Height; y++)
            for (var x = 0; x < frame.Width; x++)
            {
                var i = (y * frame.Width + x) * 4;
                if (frame.Rgba[i + 3] <= 128 || !match(frame.Rgba[i], frame.Rgba[i + 1], frame.Rgba[i + 2])) continue;
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
            }
        return right < 0 ? null : [left, top, right, bottom];
    }

    public static string Run(string tool, params string[] arguments)
    {
        var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{tool}: {error.Result}");
        return output;
    }
}

using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LoquendoAI.App;

/// <summary>PNG files with bytes after IEND can open in VEGAS but confuse FFmpeg's
/// image loop. Preserve the original path unless that precise defect is present.</summary>
internal static class ScenePngCache
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static async Task<string> GetInputAsync(string source, string projectRoot)
    {
        if (!Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase)) return source;
        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException("No se encuentra la imagen de la escena.", source);

        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var end = FindPngEnd(input);
        if (end == 0 || end == input.Length) return source;

        // Copy only original bytes through the PNG IEND chunk. The pixels, alpha,
        // metadata and every editor setting remain untouched. No image is re-encoded.
        var stamp = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{end}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..24];
        var folder = Path.Combine(projectRoot, "generated", "png_compatibles");
        var destination = Path.Combine(folder, $"imagen_{hash}.png");
        if (File.Exists(destination) && new FileInfo(destination).Length == end) return destination;
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $".{Guid.NewGuid():N}.png");
        try
        {
            input.Position = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                var remaining = end;
                while (remaining > 0)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)));
                    if (read == 0) throw new EndOfStreamException($"El PNG cambió mientras se preparaba: {source}");
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    remaining -= read;
                }
            }
            File.Move(temporary, destination, true);
            return destination;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { /* Un nuevo intento usará otra ruta temporal. */ }
        }
    }

    private static long FindPngEnd(Stream input)
    {
        Span<byte> header = stackalloc byte[8];
        if (input.Length < 20) return 0;
        input.ReadExactly(header);
        if (!header.SequenceEqual(Signature)) return 0;
        while (input.Length - input.Position >= 12)
        {
            input.ReadExactly(header);
            var size = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (size > input.Length - input.Position - 4) return 0;
            var iend = header[4] == (byte)'I' && header[5] == (byte)'E' &&
                header[6] == (byte)'N' && header[7] == (byte)'D';
            if (iend && size != 0) return 0;
            input.Seek(size, SeekOrigin.Current);
            if (iend)
            {
                Span<byte> crc = stackalloc byte[4];
                input.ReadExactly(crc);
                return BinaryPrimitives.ReadUInt32BigEndian(crc) == 0xAE426082 ? input.Position : 0;
            }
            input.Seek(4, SeekOrigin.Current);
        }
        return 0;
    }
}

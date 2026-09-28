namespace LoquendoAI.TtsBridge32;

internal sealed record WavInfo(
    long FileBytes,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    long DataBytes,
    double DurationSeconds);

internal static class WavInspector
{
    internal static WavInfo Inspect(string path)
    {
        var full = Path.GetFullPath(path);
        using var fs = File.OpenRead(full);
        using var br = new BinaryReader(fs);

        if (fs.Length < 44)
            throw new InvalidDataException("WAV demasiado pequeño.");

        var riff = new string(br.ReadChars(4));
        _ = br.ReadUInt32();
        var wave = new string(br.ReadChars(4));
        if (riff != "RIFF" || wave != "WAVE")
            throw new InvalidDataException("El archivo no es RIFF/WAVE.");

        int sampleRate = 0;
        int channels = 0;
        int bits = 0;
        int byteRate = 0;
        long dataBytes = 0;

        while (fs.Position + 8 <= fs.Length)
        {
            var chunkId = new string(br.ReadChars(4));
            var chunkSize = br.ReadUInt32();
            var chunkStart = fs.Position;

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                _ = br.ReadUInt16(); // audio format
                channels = br.ReadUInt16();
                sampleRate = (int)br.ReadUInt32();
                byteRate = (int)br.ReadUInt32();
                _ = br.ReadUInt16(); // block align
                bits = br.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                dataBytes = chunkSize;
            }

            var next = chunkStart + chunkSize + (chunkSize % 2);
            if (next > fs.Length) break;
            fs.Position = next;
        }

        if (sampleRate <= 0 || channels <= 0 || dataBytes <= 0)
            throw new InvalidDataException("WAV sin fmt/data válidos.");

        if (byteRate <= 0 && bits > 0)
            byteRate = sampleRate * channels * Math.Max(1, bits / 8);
        var duration = byteRate > 0 ? (double)dataBytes / byteRate : 0;

        return new WavInfo(fs.Length, sampleRate, channels, bits, dataBytes, duration);
    }
}

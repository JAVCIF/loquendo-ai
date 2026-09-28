using System.Runtime.InteropServices;
using System.Text;

namespace LoquendoAI.TtsBridge32;

internal sealed class LoquendoTts7Provider : IDisposable
{
    // Loquendo TTS7 exports use the Win32 stdcall ABI. On x86, using cdecl can
    // appear to work for a call and then corrupt the stack on the next native call.
    // Keep every LTTS7 delegate on StdCall; the ABI probe in hotfix 4 verified this
    // against a real TTS7 installation (ttsNewReader + ttsLoadPersona).
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int TtsNewSession(out IntPtr session, string? sessionFile);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TtsDeleteSession(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TtsNewReader(out IntPtr reader, IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int TtsLoadPersona(IntPtr reader, string voice, string? language, string? reserved);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int TtsSetAudio(IntPtr reader, string device, string? filename, uint sampleRate, int coding, int channels, IntPtr reserved);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int TtsRead(IntPtr reader, string text, [MarshalAs(UnmanagedType.I1)] bool async, [MarshalAs(UnmanagedType.I1)] bool fromFile, IntPtr reserved);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TtsSetSpeed(IntPtr reader, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TtsSetPitch(IntPtr reader, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TtsSetVolume(IntPtr reader, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int TtsQuery(IntPtr session, int queryType, string attribute, string? filter, IntPtr buffer, uint bufferSize, [MarshalAs(UnmanagedType.I1)] bool b1, [MarshalAs(UnmanagedType.I1)] bool b2);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr TtsGetErrorMessage(int result);

    private readonly IntPtr _library;
    private readonly TtsNewSession? _newSession;
    private readonly TtsDeleteSession? _deleteSession;
    private readonly TtsNewReader _newReader;
    private readonly TtsLoadPersona _loadPersona;
    private readonly TtsSetAudio _setAudio;
    private readonly TtsRead _read;
    private readonly TtsSetSpeed? _setSpeed;
    private readonly TtsSetPitch? _setPitch;
    private readonly TtsSetVolume? _setVolume;
    private readonly TtsQuery _query;
    private readonly TtsGetErrorMessage? _getErrorMessage;

    internal string DllPath { get; }
    internal string? SessionPath { get; }

    internal LoquendoTts7Provider(string? enginePath = null)
    {
        DllPath = ResolveDllPath(enginePath)
            ?? throw new FileNotFoundException("No se encontró LoqTTS7.dll. Indica --engine o revisa SOFTWARE\\Loquendo\\LTTS7\\Engine\\DataPath.");

        SessionPath = ResolveSessionPath(enginePath, DllPath);
        _library = NativeLibrary.Load(DllPath);

        _newSession = TryLoad<TtsNewSession>("ttsNewSession");
        _deleteSession = TryLoad<TtsDeleteSession>("ttsDeleteSession");
        _newReader = Load<TtsNewReader>("ttsNewReader");
        _loadPersona = Load<TtsLoadPersona>("ttsLoadPersona");
        _setAudio = Load<TtsSetAudio>("ttsSetAudio");
        _read = Load<TtsRead>("ttsRead");
        _setSpeed = TryLoad<TtsSetSpeed>("ttsSetSpeed");
        _setPitch = TryLoad<TtsSetPitch>("ttsSetPitch");
        _setVolume = TryLoad<TtsSetVolume>("ttsSetVolume");
        _query = Load<TtsQuery>("ttsQuery");
        _getErrorMessage = TryLoad<TtsGetErrorMessage>("ttsGetErrorMessage");
    }

    internal IReadOnlyList<string> GetVoices()
    {
        const int size = 64 * 1024;
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, ptr, size);
            var rc = _query(IntPtr.Zero, 1, "Id", null, ptr, size, false, false);
            ThrowIfError(rc, "ttsQuery");
            var text = Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
            return text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    internal void Synthesize(SynthRequest request, TimeSpan? timeout = null)
    {
        // Use the implicit LTTS7 session. This mirrors the known-working win32-loquendo
        // binding: ttsNewReader(&reader, NULL). Some LTTS7 distributions expose
        // ttsNewSession but are unstable when a file-based session is forced from FFI.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Output))!);
        var output = Path.GetFullPath(request.Output);
        if (File.Exists(output)) File.Delete(output);

        Trace($"DLL loaded: {DllPath}");
        Trace("ttsNewReader(NULL) -> entering");
        var rc = _newReader(out var reader, IntPtr.Zero);
        Trace($"ttsNewReader(NULL) -> returned {rc}, reader=0x{reader.ToInt64():X}");
        if (rc != 0 || reader == IntPtr.Zero)
            ThrowIfError(rc == 0 ? -1 : rc, "ttsNewReader");

        Trace($"ttsLoadPersona('{request.Voice}') -> entering");
        rc = _loadPersona(reader, request.Voice, null, null);
        Trace($"ttsLoadPersona('{request.Voice}') -> returned {rc}");
        ThrowIfError(rc, $"ttsLoadPersona('{request.Voice}')");

        if (request.Rate is int speed)
        {
            if (_setSpeed is null)
                throw new NotSupportedException("Esta instalación de TTS7 no exporta ttsSetSpeed.");
            speed = Math.Clamp(speed, 0, 100);
            Trace($"ttsSetSpeed({speed}) -> entering");
            rc = _setSpeed(reader, (uint)speed);
            Trace($"ttsSetSpeed -> returned {rc}");
            ThrowIfError(rc, "ttsSetSpeed");
        }

        if (request.Pitch is int pitch)
        {
            if (_setPitch is null)
                throw new NotSupportedException("Esta instalación de TTS7 no exporta ttsSetPitch.");
            pitch = Math.Clamp(pitch, 0, 100);
            Trace($"ttsSetPitch({pitch}) -> entering");
            rc = _setPitch(reader, (uint)pitch);
            Trace($"ttsSetPitch -> returned {rc}");
            ThrowIfError(rc, "ttsSetPitch");
        }

        if (request.Volume is int volume)
        {
            if (_setVolume is null)
                throw new NotSupportedException("Esta instalación de TTS7 no exporta ttsSetVolume.");
            volume = Math.Clamp(volume, 0, 100);
            Trace($"ttsSetVolume({volume}) -> entering");
            rc = _setVolume(reader, (uint)volume);
            Trace($"ttsSetVolume -> returned {rc}");
            ThrowIfError(rc, "ttsSetVolume");
        }

        Trace($"ttsSetAudio(LTTS7AudioFile, '{output}', {request.SampleRate}, channels={request.Channels}) -> entering");
        rc = _setAudio(reader, "LTTS7AudioFile", output, (uint)request.SampleRate, 0, request.Channels, IntPtr.Zero);
        Trace($"ttsSetAudio -> returned {rc}");
        ThrowIfError(rc, "ttsSetAudio");

        var text = BreakLongLines(request.Text, 120);
        Trace($"ttsRead(buffer, blocking) -> entering; chars={text.Length}");
        rc = _read(reader, text, false, false, IntPtr.Zero);
        Trace($"ttsRead -> returned {rc}");
        ThrowIfError(rc, "ttsRead");

        if (!File.Exists(output))
            throw new IOException("Loquendo terminó sin error, pero no creó el WAV solicitado.");
        var length = new FileInfo(output).Length;
        if (length <= 44)
            throw new IOException("Loquendo creó un WAV vacío o incompleto.");
        Trace($"WAV verified: {length} bytes");

        // Intentionally do NOT call ttsDeleteSession/FreeLibrary in this short-lived
        // native worker. LTTS7 keeps process-global state and some old Windows builds
        // crash during explicit teardown. The worker process is the cleanup boundary.
    }

    private static void Trace(string message)
    {
        Console.Error.WriteLine($"[LTTS7] {message}");
        Console.Error.Flush();
    }

    private void ThrowIfError(int rc, string operation)
    {
        if (rc == 0) return;
        var detail = GetErrorMessage(rc);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
            ? $"{operation} devolvió {rc}."
            : $"{operation} devolvió {rc}: {detail}");
    }

    private string? GetErrorMessage(int rc)
    {
        if (_getErrorMessage is null) return null;
        try
        {
            var p = _getErrorMessage(rc);
            return p == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p);
        }
        catch { return null; }
    }

    private T Load<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    private T? TryLoad<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_library, name, out var ptr) || ptr == IntPtr.Zero) return null;
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    private static string? ResolveDllPath(string? enginePath)
    {
        if (!string.IsNullOrWhiteSpace(enginePath))
        {
            var p = Environment.ExpandEnvironmentVariables(enginePath);
            if (File.Exists(p) && string.Equals(Path.GetFileName(p), "LoqTTS7.dll", StringComparison.OrdinalIgnoreCase)) return p;
            var candidate = Path.Combine(p, "bin", "LoqTTS7.dll");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(p, "LoqTTS7.dll");
            if (File.Exists(candidate)) return candidate;
        }

        var dataPath = WindowsRegistryProbe.FindLoquendoDataPath();
        if (string.IsNullOrWhiteSpace(dataPath)) return null;
        foreach (var candidate in new[]
        {
            Path.Combine(dataPath, "bin", "LoqTTS7.dll"),
            Path.Combine(dataPath, "bin", "LoqTTS7"),
            Path.Combine(dataPath, "LoqTTS7.dll")
        })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? ResolveSessionPath(string? enginePath, string dllPath)
    {
        if (!string.IsNullOrWhiteSpace(enginePath))
        {
            var p = Environment.ExpandEnvironmentVariables(enginePath);
            if (File.Exists(p) && string.Equals(Path.GetFileName(p), "default.session", StringComparison.OrdinalIgnoreCase)) return p;
            if (Directory.Exists(p))
            {
                foreach (var candidate in new[]
                {
                    Path.Combine(p, "bin", "default.session"),
                    Path.Combine(p, "default.session")
                })
                    if (File.Exists(candidate)) return candidate;
            }
        }

        var bin = Path.GetDirectoryName(dllPath);
        if (!string.IsNullOrWhiteSpace(bin))
        {
            var candidate = Path.Combine(bin, "default.session");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(bin)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                candidate = Path.Combine(parent, "default.session");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static string BreakLongLines(string input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var output = new StringBuilder();
        foreach (var originalLine in input.Replace("\r\n", "\n").Split('\n'))
        {
            var words = originalLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new StringBuilder();
            foreach (var word0 in words)
            {
                var word = word0;
                if (line.Length > 0 && line.Length + 1 + word.Length > maxLength)
                {
                    output.AppendLine(line.ToString());
                    line.Clear();
                }
                while (word.Length > maxLength)
                {
                    output.AppendLine(word[..maxLength]);
                    word = word[maxLength..];
                }
                if (word.Length == 0) continue;
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            output.AppendLine(line.ToString());
        }
        return output.ToString().TrimEnd();
    }

    public void Dispose()
    {
        // Intentionally do not FreeLibrary here. TTS7 keeps process-global native
        // state and unloading LoqTTS7.dll after creating a reader can terminate the
        // process before managed code gets control back. The isolated x86 bridge
        // releases the module naturally when the process exits.
        GC.SuppressFinalize(this);
    }
}

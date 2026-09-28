using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LoquendoAI.TtsBridge32;

internal static class LoquendoNativeAbiProbe
{
    internal sealed record ProbeResult(string Abi, bool Success, int ExitCode, string Output, string Error);

    internal static IReadOnlyList<ProbeResult> Run(string voice, string? enginePath)
    {
        return new[]
        {
            RunOne("cdecl", voice, enginePath),
            RunOne("stdcall", voice, enginePath)
        };
    }

    private static ProbeResult RunOne(string abi, string voice, string? enginePath)
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("No se pudo determinar la ruta del TTS bridge.");
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        p.StartInfo.ArgumentList.Add("native-abi-worker");
        p.StartInfo.ArgumentList.Add("--abi");
        p.StartInfo.ArgumentList.Add(abi);
        p.StartInfo.ArgumentList.Add("--voice");
        p.StartInfo.ArgumentList.Add(voice);
        if (!string.IsNullOrWhiteSpace(enginePath))
        {
            p.StartInfo.ArgumentList.Add("--engine");
            p.StartInfo.ArgumentList.Add(enginePath);
        }
        p.Start();
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);
        return new ProbeResult(abi, p.ExitCode == 0, p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    internal static void Worker(string abi, string voice, string? enginePath)
    {
        var dll = ResolveDll(enginePath)
            ?? throw new FileNotFoundException("No se encontró LoqTTS7.dll.");
        var lib = NativeLibrary.Load(dll);
        Console.Error.WriteLine($"[ABI:{abi}] DLL loaded: {dll}");
        Console.Error.Flush();

        if (abi.Equals("stdcall", StringComparison.OrdinalIgnoreCase))
            ProbeStdCall(lib, voice);
        else if (abi.Equals("cdecl", StringComparison.OrdinalIgnoreCase))
            ProbeCdecl(lib, voice);
        else
            throw new ArgumentException($"ABI desconocida: {abi}");

        Console.WriteLine($"OK {abi}: ttsNewReader + ttsLoadPersona('{voice}')");
        Console.Out.Flush();
        // Do not unload/delete legacy LTTS state. Process exit is the cleanup boundary.
        Environment.Exit(0);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NewReaderCdecl(out IntPtr reader, IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int LoadPersonaCdecl(IntPtr reader, string voice, string? language, string? reserved);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int NewReaderStdCall(out IntPtr reader, IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int LoadPersonaStdCall(IntPtr reader, string voice, string? language, string? reserved);

    private static void ProbeCdecl(IntPtr lib, string voice)
    {
        var nr = Marshal.GetDelegateForFunctionPointer<NewReaderCdecl>(NativeLibrary.GetExport(lib, "ttsNewReader"));
        var lp = Marshal.GetDelegateForFunctionPointer<LoadPersonaCdecl>(NativeLibrary.GetExport(lib, "ttsLoadPersona"));
        Console.Error.WriteLine("[ABI:cdecl] ttsNewReader -> entering"); Console.Error.Flush();
        var rc = nr(out var reader, IntPtr.Zero);
        Console.Error.WriteLine($"[ABI:cdecl] ttsNewReader -> {rc}, reader=0x{reader.ToInt64():X}"); Console.Error.Flush();
        if (rc != 0 || reader == IntPtr.Zero) throw new InvalidOperationException($"ttsNewReader devolvió {rc}");
        Console.Error.WriteLine($"[ABI:cdecl] ttsLoadPersona('{voice}') -> entering"); Console.Error.Flush();
        rc = lp(reader, voice, null, null);
        Console.Error.WriteLine($"[ABI:cdecl] ttsLoadPersona -> {rc}"); Console.Error.Flush();
        if (rc != 0) throw new InvalidOperationException($"ttsLoadPersona devolvió {rc}");
    }

    private static void ProbeStdCall(IntPtr lib, string voice)
    {
        var nr = Marshal.GetDelegateForFunctionPointer<NewReaderStdCall>(NativeLibrary.GetExport(lib, "ttsNewReader"));
        var lp = Marshal.GetDelegateForFunctionPointer<LoadPersonaStdCall>(NativeLibrary.GetExport(lib, "ttsLoadPersona"));
        Console.Error.WriteLine("[ABI:stdcall] ttsNewReader -> entering"); Console.Error.Flush();
        var rc = nr(out var reader, IntPtr.Zero);
        Console.Error.WriteLine($"[ABI:stdcall] ttsNewReader -> {rc}, reader=0x{reader.ToInt64():X}"); Console.Error.Flush();
        if (rc != 0 || reader == IntPtr.Zero) throw new InvalidOperationException($"ttsNewReader devolvió {rc}");
        Console.Error.WriteLine($"[ABI:stdcall] ttsLoadPersona('{voice}') -> entering"); Console.Error.Flush();
        rc = lp(reader, voice, null, null);
        Console.Error.WriteLine($"[ABI:stdcall] ttsLoadPersona -> {rc}"); Console.Error.Flush();
        if (rc != 0) throw new InvalidOperationException($"ttsLoadPersona devolvió {rc}");
    }

    private static string? ResolveDll(string? enginePath)
    {
        if (!string.IsNullOrWhiteSpace(enginePath))
        {
            var p = Environment.ExpandEnvironmentVariables(enginePath);
            if (File.Exists(p)) return p;
            foreach (var c in new[] { Path.Combine(p, "bin", "LoqTTS7.dll"), Path.Combine(p, "LoqTTS7.dll") })
                if (File.Exists(c)) return c;
        }
        var dataPath = WindowsRegistryProbe.FindLoquendoDataPath();
        if (string.IsNullOrWhiteSpace(dataPath)) return null;
        foreach (var c in new[] { Path.Combine(dataPath, "bin", "LoqTTS7.dll"), Path.Combine(dataPath, "LoqTTS7.dll") })
            if (File.Exists(c)) return c;
        return null;
    }

    internal static string FormatExitCode(int code) => $"{code} (0x{unchecked((uint)code):X8})";
}

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LoquendoAI.TtsBridge32;

internal static class TtsQaRunner
{
    private sealed record QaCase(string Id, string Text);

    internal static int RunQaSuite(string provider, string voice, string outDir, string? engine)
    {
        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);

        var cases = new[]
        {
            new QaCase("01_basico", "Hola amigos de YouTube, esto es una prueba de Loquendo AI."),
            new QaCase("02_acentos", "El pingüino llegó a Bogotá con información, canción, acción, corazón, ñandú y vergüenza."),
            new QaCase("03_signos", "¿Qué pasó? ¡Nada! Bueno... quizá sí; pero, en fin: seguimos probando."),
            new QaCase("04_numeros", "Prueba con números: 0, 7, 42, 1999, 2026, 3.1416, 50 por ciento y 12:34."),
            new QaCase("05_simbolos", "Símbolos para escuchar manualmente: arroba, numeral, más, menos, igual, barra, porcentaje y euro €."),
            new QaCase("06_comillas", "Carlos dijo: \"esto debería sonar normal\". Luego preguntó: '¿verdad?'."),
            new QaCase("07_multilinea", "Primera línea.\nSegunda línea después de un salto.\nTercera línea para comprobar continuidad."),
            new QaCase("08_larga", BuildLongText()),
            new QaCase("09_nombres", "Jorge, Carlos, Carmen, Diego, Ximena, Soledad y Esperanza participan en esta prueba."),
            new QaCase("10_espanol_extendido", "México, Bogotá, pingüino, lingüística, François, Müller, São Paulo y España."),
        };

        var report = new StringBuilder();
        report.AppendLine("id,status,bytes,sample_rate,channels,bits,duration_seconds,elapsed_ms,error");
        var failures = 0;
        Console.WriteLine($"QA TTS: provider={provider}, voice={voice}");
        Console.WriteLine($"Salida: {outDir}");

        foreach (var c in cases)
        {
            var output = Path.Combine(outDir, c.Id + ".wav");
            var sw = Stopwatch.StartNew();
            try
            {
                Synthesize(provider, voice, c.Text, output, engine, rate: null, volume: null, verboseNative: false);
                sw.Stop();
                var wav = WavInspector.Inspect(output);
                Console.WriteLine($"[OK] {c.Id,-22} {wav.DurationSeconds,6:F2}s  {wav.FileBytes,9} bytes  {sw.ElapsedMilliseconds,6} ms");
                report.AppendLine($"{Csv(c.Id)},OK,{wav.FileBytes},{wav.SampleRate},{wav.Channels},{wav.BitsPerSample},{wav.DurationSeconds.ToString("F3", CultureInfo.InvariantCulture)},{sw.ElapsedMilliseconds},");
            }
            catch (Exception ex)
            {
                sw.Stop();
                failures++;
                Console.WriteLine($"[FAIL] {c.Id}: {ex.Message}");
                report.AppendLine($"{Csv(c.Id)},FAIL,,,,,,{sw.ElapsedMilliseconds},{Csv(ex.Message)}");
            }
        }

        File.WriteAllText(Path.Combine(outDir, "qa_report.csv"), report.ToString(), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(outDir, "LEEME.txt"),
            "Escucha manualmente los WAV. El verificador automático sólo confirma que el archivo RIFF/WAVE es válido y mide sus propiedades; no puede decidir si una voz pronunció correctamente el texto.\r\n" +
            "Especial atención: 02_acentos, 03_signos, 07_multilinea, 08_larga y 10_espanol_extendido.\r\n",
            new UTF8Encoding(true));

        Console.WriteLine(failures == 0
            ? $"QA completado: {cases.Length}/{cases.Length} WAV válidos."
            : $"QA completado con {failures} fallo(s). Revisa qa_report.csv.");
        return failures;
    }

    internal static int RunStress(string provider, string voice, int count, string outDir, string? engine)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), "--count debe ser >= 1.");
        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);
        var scratch = Path.Combine(outDir, "_stress_current.wav");
        var reportPath = Path.Combine(outDir, $"stress_{Sanitize(provider)}_{Sanitize(voice)}_{count}.csv");
        var report = new StringBuilder();
        report.AppendLine("iteration,status,bytes,duration_seconds,elapsed_ms,error");

        var total = Stopwatch.StartNew();
        var failures = 0;
        long totalBytes = 0;
        double totalDuration = 0;
        var progressStep = Math.Max(1, count / 20);
        var samples = new HashSet<int> { 1, 2, 3, Math.Max(1, count / 2), count };

        Console.WriteLine($"Stress TTS: provider={provider}, voice={voice}, count={count}");
        Console.WriteLine("Cada iteración pasa por el mismo camino seguro que usará Loquendo AI; TTS7 se ejecuta en worker x86 aislado.");

        for (var i = 1; i <= count; i++)
        {
            var text = $"Prueba de estrés número {i} de {count}. Bogotá, pingüino, acción y corazón. Esta línea cambia para evitar reutilizar exactamente el mismo texto.";
            var sw = Stopwatch.StartNew();
            try
            {
                Synthesize(provider, voice, text, scratch, engine, rate: null, volume: null, verboseNative: false);
                sw.Stop();
                var wav = WavInspector.Inspect(scratch);
                totalBytes += wav.FileBytes;
                totalDuration += wav.DurationSeconds;
                report.AppendLine($"{i},OK,{wav.FileBytes},{wav.DurationSeconds.ToString("F3", CultureInfo.InvariantCulture)},{sw.ElapsedMilliseconds},");

                if (samples.Contains(i))
                {
                    var samplePath = Path.Combine(outDir, $"sample_{i:D5}.wav");
                    File.Copy(scratch, samplePath, true);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                failures++;
                report.AppendLine($"{i},FAIL,,,{sw.ElapsedMilliseconds},{Csv(ex.Message)}");
                Console.WriteLine($"[FAIL] iteración {i}: {ex.Message}");
                // Stop on first native/engine failure: later iterations usually add noise, not information.
                break;
            }

            if (i == 1 || i % progressStep == 0 || i == count)
            {
                var rate = i / Math.Max(0.001, total.Elapsed.TotalSeconds);
                Console.WriteLine($"[{i,5}/{count}] OK  {rate:F2} líneas/s  elapsed={total.Elapsed:hh\\:mm\\:ss}");
            }
        }

        total.Stop();
        try { if (File.Exists(scratch)) File.Delete(scratch); } catch { }
        File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true));

        var completed = report.ToString().Split('\n').Count(x => x.Contains(",OK,"));
        Console.WriteLine();
        Console.WriteLine($"Completadas: {completed}/{count}");
        Console.WriteLine($"Fallos: {failures}");
        Console.WriteLine($"Tiempo: {total.Elapsed}");
        if (completed > 0)
        {
            Console.WriteLine($"Promedio WAV: {(double)totalBytes / completed / 1024:F1} KiB");
            Console.WriteLine($"Audio sintetizado acumulado: {totalDuration / 60:F2} min");
        }
        Console.WriteLine($"Reporte: {reportPath}");
        return failures;
    }

    internal static int RunControls(string provider, string voice, string outDir, string? engine)
    {
        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);
        const string text = "Hola amigos de YouTube. Esta frase sirve para comparar la velocidad y el volumen de la voz.";
        var failures = 0;

        if (provider.Equals("loquendo7-native", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("loquendo", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var speed in new[] { 30, 50, 70 })
            {
                var output = Path.Combine(outDir, $"tts7_speed_{speed}.wav");
                try
                {
                    Synthesize(provider, voice, text, output, engine, speed, null, false);
                    var wav = WavInspector.Inspect(output);
                    Console.WriteLine($"[OK] speed={speed}: {wav.DurationSeconds:F2}s");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] speed={speed}: {ex.Message}");
                }
            }
            foreach (var pitch in new[] { 25, 50, 75 })
            {
                var output = Path.Combine(outDir, $"tts7_pitch_{pitch}.wav");
                try
                {
                    Synthesize(provider, voice, text, output, engine, null, null, false, pitch);
                    _ = WavInspector.Inspect(output);
                    Console.WriteLine($"[OK] pitch={pitch}");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] pitch={pitch}: {ex.Message}");
                }
            }
            foreach (var volume in new[] { 25, 50, 75 })
            {
                var output = Path.Combine(outDir, $"tts7_volume_{volume}.wav");
                try
                {
                    Synthesize(provider, voice, text, output, engine, null, volume, false);
                    _ = WavInspector.Inspect(output);
                    Console.WriteLine($"[OK] volume={volume}");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] volume={volume}: {ex.Message}");
                }
            }
            File.WriteAllText(Path.Combine(outDir, "CONTROLS_README.txt"),
                "TTS7: velocidad, pitch y volumen se prueban ahora mediante las APIs nativas ttsSetSpeed/ttsSetPitch/ttsSetVolume. Escucha los WAV para validar la diferencia perceptual.\r\n",
                new UTF8Encoding(true));
        }
        else if (provider.Equals("sapi5-x86", StringComparison.OrdinalIgnoreCase) || provider.Equals("sapi", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var rate in new[] { -4, 0, 4 })
            {
                var output = Path.Combine(outDir, $"sapi_rate_{rate:+0;-0;0}.wav");
                try
                {
                    Synthesize(provider, voice, text, output, engine, rate, 100, false);
                    var wav = WavInspector.Inspect(output);
                    Console.WriteLine($"[OK] rate={rate}: {wav.DurationSeconds:F2}s");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] rate={rate}: {ex.Message}");
                }
            }
            foreach (var volume in new[] { 35, 65, 100 })
            {
                var output = Path.Combine(outDir, $"sapi_volume_{volume}.wav");
                try
                {
                    Synthesize(provider, voice, text, output, engine, 0, volume, false);
                    _ = WavInspector.Inspect(output);
                    Console.WriteLine($"[OK] volume={volume}");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] volume={volume}: {ex.Message}");
                }
            }
            foreach (var pitch in new[] { -5, 0, 5 })
            {
                var output = Path.Combine(outDir, $"sapi_pitch_{pitch:+0;-0;0}.wav");
                try
                {
                    Synthesize(provider, voice, text, output, engine, null, null, false, pitch);
                    _ = WavInspector.Inspect(output);
                    Console.WriteLine($"[OK] pitch={pitch} (SAPI XML; el soporte final depende de la voz)");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] pitch={pitch}: {ex.Message}");
                }
            }
        }
        else
        {
            throw new ArgumentException("controls-test soporta por ahora loquendo7-native y sapi5-x86.");
        }

        return failures;
    }

    private static void Synthesize(string provider, string voice, string text, string output, string? engine, int? rate, int? volume, bool verboseNative, int? pitch = null)
    {
        var request = new SynthRequest(provider, voice, text, output, rate, volume, 32000, 1, engine, pitch);
        switch (provider.ToLowerInvariant())
        {
            case "loquendo7-native":
            case "loquendo":
                LoquendoNativeIsolation.Synthesize(request, verboseNative);
                break;
            case "sapi5-x86":
            case "sapi":
                Sapi5Provider.Synthesize(request);
                break;
            default:
                throw new ArgumentException($"Provider no soportado por QA: {provider}");
        }
    }

    private static string BuildLongText()
    {
        var sentence = "Esta es una oración deliberadamente larga para verificar que Loquendo AI divida el texto sin cortar palabras y pueda sintetizar entradas extensas manteniendo la puntuación correctamente. ";
        return string.Concat(Enumerable.Repeat(sentence, 8));
    }

    private static string Csv(object? value)
    {
        var s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return '"' + s.Replace("\"", "\"\"") + '"';
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value.Replace(' ', '_');
    }
}

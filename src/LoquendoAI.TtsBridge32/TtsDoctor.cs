using System.Diagnostics;

namespace LoquendoAI.TtsBridge32;

internal static class TtsDoctor
{
    internal static IReadOnlyList<ProviderInfo> Scan(string? engineOverride = null)
    {
        var result = new List<ProviderInfo>();

        // SAPI 5 x86
        try
        {
            var voices = Sapi5Provider.GetVoices();
            result.Add(new ProviderInfo("sapi5-x86", "Microsoft SAPI 5 (32-bit)", voices.Count > 0,
                voices.Count > 0 ? $"{voices.Count} voces" : "SAPI accesible, sin voces detectadas", Voices: voices));
        }
        catch (Exception ex)
        {
            result.Add(new ProviderInfo("sapi5-x86", "Microsoft SAPI 5 (32-bit)", false, ex.Message));
        }

        // Loquendo TTS7 native
        try
        {
            using var loq = new LoquendoTts7Provider(engineOverride);
            var voices = loq.GetVoices();
            result.Add(new ProviderInfo("loquendo7-native", "Loquendo TTS7 Native", true,
                $"{voices.Count} voces; acceso directo a LoqTTS7.dll; session: {loq.SessionPath ?? "predeterminada del motor"}", loq.DllPath, null, voices));
        }
        catch (Exception ex)
        {
            result.Add(new ProviderInfo("loquendo7-native", "Loquendo TTS7 Native", false, ex.Message));
        }

        // Balabolka / BALCON
        var bal = ProgramLocator.InspectExecutable("balabolka-gui", "Balabolka GUI", ProgramLocator.BalabolkaCandidates());
        result.Add(bal);
        var balconPath = BalconProvider.Find();
        if (balconPath is not null)
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(balconPath);
                var voices = BalconProvider.GetVoices(balconPath);
                var sapi4Voices = BalconProvider.GetVoices(balconPath, sapi4Only: true);
                var package = BalconProvider.GetCompanionSummary(balconPath);
                result.Add(new ProviderInfo("balcon", "Balabolka Console (BALCON)", true,
                    $"{voices.Count} voces totales; {sapi4Voices.Count} SAPI4; {package}", balconPath, vi.FileVersion ?? vi.ProductVersion, voices));
            }
            catch (Exception ex)
            {
                result.Add(new ProviderInfo("balcon", "Balabolka Console (BALCON)", true,
                    $"Detectado, pero falló el probe: {ex.Message}", balconPath));
            }
        }
        else
        {
            result.Add(new ProviderInfo("balcon", "Balabolka Console (BALCON)", false,
                "No se encontró balcon.exe. El GUI de Balabolka no se automatiza por clicks."));
        }

        // Infovox / SpeechPad
        var infovox = WindowsRegistryProbe.FindInfovoxInstallPath();
        var speechPad = ProgramLocator.InspectExecutable("speechpad", "Infovox SpeechPad 2.2", ProgramLocator.SpeechPadCandidates(infovox));
        var acatts = infovox is null ? null : Path.Combine(infovox, "acatts.dll");
        var sapiDll = infovox is null ? null : Path.Combine(infovox, "AcaTtsSapi5.dll");
        var detail = speechPad.Detail;
        if (File.Exists(acatts)) detail += "; acatts.dll detectado";
        if (File.Exists(sapiDll)) detail += "; AcaTtsSapi5.dll detectado (usable vía SAPI5)";
        result.Add(speechPad with { Detail = detail, Path = speechPad.Path ?? infovox });

        // TextAloud - detection only for now.
        result.Add(ProgramLocator.InspectExecutable("textaloud", "TextAloud", ProgramLocator.TextAloudCandidates()));

        return result;
    }
}

using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace LoquendoAI.TtsBridge32;

internal static class Sapi5Provider
{
    internal static IReadOnlyList<string> GetVoices()
    {
        object? voiceObj = null;
        object? tokensObj = null;
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null) return WindowsRegistryProbe.EnumerateSapi5RegistryVoices();
            voiceObj = Activator.CreateInstance(type);
            if (voiceObj is null) return WindowsRegistryProbe.EnumerateSapi5RegistryVoices();
            dynamic voice = voiceObj;
            tokensObj = voice.GetVoices("", "");
            dynamic tokens = tokensObj;
            var list = new List<string>();
            for (var i = 0; i < (int)tokens.Count; i++)
            {
                object? tokenObj = null;
                try
                {
                    tokenObj = tokens.Item(i);
                    dynamic token = tokenObj;
                    list.Add((string)token.GetDescription(0));
                }
                finally { ReleaseCom(tokenObj); }
            }
            return list.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch
        {
            return WindowsRegistryProbe.EnumerateSapi5RegistryVoices();
        }
        finally
        {
            ReleaseCom(tokensObj);
            ReleaseCom(voiceObj);
        }
    }

    internal static void Synthesize(SynthRequest request)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Output))!);
        object? voiceObj = null;
        object? streamObj = null;
        object? tokensObj = null;
        object? selectedTokenObj = null;
        try
        {
            var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice")
                ?? throw new InvalidOperationException("SAPI.SpVoice no está registrado en este entorno x86.");
            var streamType = Type.GetTypeFromProgID("SAPI.SpFileStream")
                ?? throw new InvalidOperationException("SAPI.SpFileStream no está registrado.");

            voiceObj = Activator.CreateInstance(voiceType)
                ?? throw new InvalidOperationException("No se pudo crear SAPI.SpVoice.");
            streamObj = Activator.CreateInstance(streamType)
                ?? throw new InvalidOperationException("No se pudo crear SAPI.SpFileStream.");

            dynamic voice = voiceObj;
            tokensObj = voice.GetVoices("", "");
            dynamic tokens = tokensObj;

            // First require an exact description match. If an old caller persisted a voice
            // with harmless accent/case differences, use a conservative normalized fallback.
            selectedTokenObj = FindToken(tokens, request.Voice, exactOnly: true)
                ?? FindToken(tokens, request.Voice, exactOnly: false)
                ?? throw new InvalidOperationException($"No se encontró la voz SAPI '{request.Voice}'.");

            // Keep the selected token RCW alive for the whole Speak call. Some old SAPI
            // compatibility engines (notably legacy Acapela/Infovox wrappers) are less
            // tolerant of aggressively FinalReleaseComObject'ing the token immediately.
            dynamic selectedToken = selectedTokenObj;
            voice.Voice = selectedToken;

            // A null rate means "engine default". Do not force Rate=0 on legacy engines.
            if (request.Rate is int rate)
                voice.Rate = Math.Clamp(rate, -10, 10);
            if (request.Volume is int volume)
                voice.Volume = Math.Clamp(volume, 0, 100);

            // SSFMCreateForWrite = 3. SAPI chooses a WAV PCM format supported by the selected voice.
            dynamic stream = streamObj;
            stream.Open(Path.GetFullPath(request.Output), 3, false);
            voice.AudioOutputStream = stream;

            // IMPORTANT: pitch=0 is neutral and must use plain text. Older SAPI compatibility
            // engines can crash inside their native DLL when handed SAPI XML even when the XML
            // requests no actual pitch change. Only opt into XML when a non-neutral pitch was asked.
            if (request.Pitch is int pitch && pitch != 0)
            {
                pitch = Math.Clamp(pitch, -10, 10);
                var escaped = SecurityElement.Escape(request.Text) ?? string.Empty;
                // SVSFIsXML = 8. Pitch XML remains engine-dependent; neutral pitch never uses it.
                voice.Speak($"<pitch middle=\"{pitch:+0;-0;0}\">{escaped}</pitch>", 8);
            }
            else
            {
                voice.Speak(request.Text, 0);
            }
            stream.Close();
        }
        finally
        {
            // Release SpVoice before its selected token; it may still reference that token.
            ReleaseCom(streamObj);
            ReleaseCom(voiceObj);
            ReleaseCom(selectedTokenObj);
            ReleaseCom(tokensObj);
        }
    }

    private static object? FindToken(dynamic tokens, string requestedVoice, bool exactOnly)
    {
        for (var i = 0; i < (int)tokens.Count; i++)
        {
            object? tokenObj = null;
            var keep = false;
            try
            {
                tokenObj = tokens.Item(i);
                dynamic token = tokenObj;
                var desc = (string)token.GetDescription(0);
                var match = exactOnly
                    ? string.Equals(desc, requestedVoice, StringComparison.CurrentCultureIgnoreCase)
                    : VoiceNamesEquivalent(desc, requestedVoice);
                if (!match) continue;
                keep = true;
                return tokenObj;
            }
            finally
            {
                if (!keep) ReleaseCom(tokenObj);
            }
        }
        return null;
    }

    private static bool VoiceNamesEquivalent(string a, string b)
        => NormalizeVoiceName(a) == NormalizeVoiceName(b);

    private static string NormalizeVoiceName(string value)
    {
        // Fallback only: case/diacritics/punctuation/spacing are ignored, but letters and digits
        // must still match. The normal path uses the exact SAPI description.
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.ToString();
    }

    private static void ReleaseCom(object? obj)
    {
        if (obj is not null && Marshal.IsComObject(obj))
        {
            try { Marshal.FinalReleaseComObject(obj); }
            catch { }
        }
    }
}

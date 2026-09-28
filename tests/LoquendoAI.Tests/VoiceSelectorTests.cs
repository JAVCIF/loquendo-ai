using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Tts;

namespace LoquendoAI.Tests;

/// <summary>Voices of the selectors (1.4.0): defaults, user choices, voices added by hand and block direct voices.</summary>
internal static class VoiceSelectorTests
{
    private static Dictionary<string, IReadOnlyList<string>> Catalog() => new(StringComparer.OrdinalIgnoreCase)
    {
        [VoiceSelectorSettings.Tts7] = ["Jorge", "Carmen", "Francisca"],
        [VoiceSelectorSettings.Sapi4] = ["Juan", "Antonio", "Carlos", "Mike in Space"],
        [VoiceSelectorSettings.Sapi5] = ["IVONA 2 Enrique", "IVONA 2 Conchita", "Microsoft Helena Desktop", "Loquendo Jorge", "ScanSoft Juan_Full_22kHz", "Juana"]
    };

    [Test("Selector de voces: por defecto todo TTS7, Juan y Antonio de SAPI4, y Juan y las IVONA de SAPI5")]
    public static void Defaults()
    {
        var visible = new VoiceSelectorSettings().Visible(Catalog()).Select(x => x.Label).ToArray();
        Assert.Equal(string.Join(" | ", new[]
        {
            "TTS7 · Carmen", "TTS7 · Francisca", "TTS7 · Jorge",
            "SAPI4 · Antonio", "SAPI4 · Juan",
            "SAPI5 · IVONA 2 Conchita", "SAPI5 · IVONA 2 Enrique", "SAPI5 · ScanSoft Juan_Full_22kHz"
        }), string.Join(" | ", visible), "orden por motor y nombre");
    }

    [Test("Selector de voces: mostrar/ocultar, añadir a mano, predeterminadas y guardado")]
    public static void Choices()
    {
        var settings = new VoiceSelectorSettings();
        settings.Set("sapi", "Microsoft Helena Desktop", true);   // alias of the bridge
        settings.Set(VoiceSelectorSettings.Tts7, "carmen", false); // case does not matter
        settings.AddManual(VoiceSelectorSettings.Sapi4, "  Sam  ");
        var visible = settings.Visible(Catalog()).Select(x => x.Label).ToArray();
        Assert.True(visible.Contains("SAPI5 · Microsoft Helena Desktop"), "SAPI5 elegida a mano");
        Assert.False(visible.Contains("TTS7 · Carmen"), "TTS7 oculta");
        Assert.True(visible.Contains("SAPI4 · Sam"), "añadida a mano aunque BALCON no la liste");
        Assert.True(settings.Known(VoiceSelectorSettings.Sapi4, Catalog()[VoiceSelectorSettings.Sapi4]).Contains("Sam"), "el editor la lista");

        var path = Path.Combine(Path.GetTempPath(), $"loquendo-selector-{Guid.NewGuid():N}.json");
        try
        {
            settings.Save(path);
            var loaded = VoiceSelectorSettings.Load(path);
            Assert.Equal(string.Join("|", visible), string.Join("|", loaded.Visible(Catalog()).Select(x => x.Label)), "se guarda y se lee igual");
            Assert.Equal(VoiceSelectorSettings.Sapi5, loaded.Voices.First(x => x.Voice.Contains("Helena")).Provider, "alias guardado con su clave");
        }
        finally { File.Delete(path); }

        settings.ResetToDefaults();
        Assert.Equal(8, settings.Visible(Catalog()).Count, "predeterminadas");
        File.WriteAllText(path, "{ roto");
        try { Assert.Equal(0, VoiceSelectorSettings.Load(path).Voices.Count, "un archivo ilegible = predeterminadas"); }
        finally { File.Delete(path); }
    }

    [Test("Voz directa del bloque: cualquier motor del selector (antes solo TTS7) y bloques antiguos")]
    public static void BlockDirectVoice()
    {
        var sapi4 = BlockParameters.Empty.WithDirectVoice(VoiceSelectorSettings.Sapi4, "Juan");
        Assert.Equal("SAPI4 · Juan", DirectVoiceRef.Of(BlockParameters.Parse(sapi4.ToJson()))?.Label, "SAPI4 se guarda y se lee");
        var old = BlockParameters.Parse("{\"directVoiceProvider\":\"loquendo7-native\",\"directVoiceId\":\"Jorge\"}");
        Assert.Equal("TTS7 · Jorge", DirectVoiceRef.Of(old)?.Label, "bloques de 1.2/1.3.0");
        Assert.True(DirectVoiceRef.Of(BlockParameters.Parse("{\"directVoiceProvider\":\"elevenlabs\",\"directVoiceId\":\"x\"}")) is null,
            "motor desconocido: sin voz directa");
        Assert.True(new DirectVoiceRef(VoiceSelectorSettings.Sapi5, "IVONA 2 Enrique").Is("sapi5-x86", "ivona 2 enrique"), "comparación sin mayúsculas");
    }
}

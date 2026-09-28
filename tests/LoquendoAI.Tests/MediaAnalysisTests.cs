using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Library;

namespace LoquendoAI.Tests;

/// <summary>«IA: analizar medios» (1.3.0): classification, names that say nothing, batched prompt and answer.</summary>
internal static class MediaAnalysisTests
{
    [Test("Medios: tipo por extensión (o por el tipo del catálogo) y audios que se pueden escuchar")]
    public static void Classify()
    {
        Assert.Equal(MediaCategory.Image, MediaAnalysis.Classify(".PNG", AssetKind.Background), "imagen");
        Assert.Equal(MediaCategory.Gif, MediaAnalysis.Classify(".gif", AssetKind.Meme), "GIF: por nombre, no por visión");
        Assert.Equal(MediaCategory.Video, MediaAnalysis.Classify("mp4", AssetKind.Video), "video sin punto");
        Assert.Equal(MediaCategory.Audio, MediaAnalysis.Classify(".ogg", AssetKind.Music), "audio");
        Assert.Equal(MediaCategory.Audio, MediaAnalysis.Classify(".xyz", AssetKind.SoundEffect), "extensión rara pero catalogado como SFX");
        Assert.Equal(MediaCategory.None, MediaAnalysis.Classify(".ttf", AssetKind.Font), "fuente: no se analiza");
        Assert.True(MediaAnalysis.CanListen(".mp3") && !MediaAnalysis.CanListen(".mid"), "MIDI no se escucha");
    }

    [Test("Medios: nombres que no dicen nada no gastan tokens")]
    public static void Generic()
    {
        foreach (var name in new[] { "track01.mp3", "sfx_023.wav", "IMG_2041.gif", "audio (3).ogg", "3f2a9c1be0d44b7a.mp3", "Pista 7.wav", "VID-20240101-WA0003.mp4" })
            Assert.True(MediaAnalysis.LooksGeneric(name), "genérico: " + name);
        foreach (var name in new[] { "Megalovania.mp3", "explosion_grande.wav", "coffin dance.mp4", "risa_malvada_02.ogg", "Bart - ay caramba.wav" })
            Assert.False(MediaAnalysis.LooksGeneric(name), "con información: " + name);
    }

    [Test("Medios: un lote por nombre (prompt compacto, esquema con refs, respuesta y confianza)")]
    public static void NameBatch()
    {
        var items = new[]
        {
            new MediaNameItem("A1", MediaCategory.Audio, AssetKind.Music, "Megalovania.mp3", "Undertale"),
            new MediaNameItem("A2", MediaCategory.Audio, AssetKind.SoundEffect, "boom.wav", ""),
            new MediaNameItem("A3", MediaCategory.Video, AssetKind.Meme, "coffin dance.mp4", "memes")
        };
        var prompt = MediaAnalysis.NamePrompt(items);
        Assert.True(prompt.Contains("A1 | música | nombre=Megalovania.mp3 | carpeta=Undertale") &&
                    prompt.Contains("A2 | efecto de sonido | nombre=boom.wav\n") && prompt.Contains("A3 | video meme"), "una línea corta por archivo");
        var answer = MediaAnalysis.ParseNameAnswer("""
            {"items":[
              {"ref":"A1","description":"Megalovania (Toby Fox, Undertale): tema de batalla rápido y frenético","role":"musica","mood":"épico","subject":"Undertale","confianza":"alta"},
              {"ref":"A2","description":"explosión","role":"sfx","mood":"","subject":"","confianza":"baja"},
              {"ref":"A3","description":"","role":"meme","mood":"","subject":"","confianza":"media"}
            ]}
            """);
        Assert.True(answer["A1"].Confident && answer["A1"].Description.StartsWith("Megalovania"), "reconocida");
        Assert.False(answer["A2"].Confident, "baja: se puede escuchar después");
        Assert.False(answer["A3"].Confident, "sin descripción no cuenta como lograda");
        Assert.Equal(3, answer.Count, "todas las refs");
    }
}

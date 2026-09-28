using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>Renders shown without a character (NPC, extras) can be hidden (1.4.1): editor data, composition, VEGAS,
/// Director language and the AI Director schema.</summary>
internal static class NpcRenderTests
{
    private static SceneScriptBlock Block(int order, ScriptBlockKind kind, Guid? character = null, Guid? asset = null, int pause = 0) =>
        new(Guid.NewGuid(), Guid.Empty, order, kind, CharacterId: character, AssetId: asset, PauseAfterMs: pause);

    [Test("Ocultar un render NPC: apunta al último «Mostrar» de ese recurso; si ya tiene personaje, al personaje")]
    public static void Target()
    {
        var render = Guid.NewGuid();
        var bart = Guid.NewGuid();
        var show = Block(0, ScriptBlockKind.CharacterShow, asset: render);
        var hide = Block(2, ScriptBlockKind.CharacterHide, asset: render);
        Assert.Equal(((Guid?)null, (Guid?)show.Id), SceneComposer.HideTarget([show, hide], hide), "el render");
        var later = Block(3, ScriptBlockKind.CharacterShow, asset: render);
        Assert.Equal(((Guid?)null, (Guid?)show.Id), SceneComposer.HideTarget([show, hide, later], hide), "solo los anteriores");
        Assert.Equal(((Guid?)bart, (Guid?)null), SceneComposer.HideTarget([show with { CharacterId = bart }, hide], hide),
            "el render recibió personaje: oculta al personaje");
        Assert.Equal(((Guid?)null, (Guid?)null), SceneComposer.HideTarget([hide], hide), "sin «Mostrar» antes: nada");
        Assert.Equal(((Guid?)bart, (Guid?)null), SceneComposer.HideTarget([], hide with { CharacterId = bart, AssetId = null }), "personaje");

        // The same render as an NPC and for Bart: the NPC is hidden, not Bart.
        var bartShow = Block(1, ScriptBlockKind.CharacterShow, bart, render);
        Assert.Equal(((Guid?)null, (Guid?)show.Id), SceneComposer.HideTarget([show, bartShow, hide], hide), "el NPC, no Bart");
        // Two extras with the same render: two hides, one each.
        var second = Block(1, ScriptBlockKind.CharacterShow, asset: render);
        var hide1 = Block(2, ScriptBlockKind.CharacterHide, asset: render);
        var hide2 = Block(3, ScriptBlockKind.CharacterHide, asset: render);
        var scene = new[] { show, second, hide1, hide2 };
        Assert.Equal(((Guid?)null, (Guid?)second.Id), SceneComposer.HideTarget(scene, hide1), "primero el último que entró");
        Assert.Equal(((Guid?)null, (Guid?)show.Id), SceneComposer.HideTarget(scene, hide2), "luego el otro");
        var extra = Block(4, ScriptBlockKind.CharacterHide, asset: render);
        Assert.Equal(((Guid?)null, (Guid?)null), SceneComposer.HideTarget([show, bartShow, hide1 with { OrderIndex = 2 }, extra], extra),
            "un «Ocultar» de más no se lleva a Bart");
    }

    [Test("Escena y VEGAS: el render NPC desaparece en el «Ocultar» y otro NPC sigue en pantalla")]
    public static async Task Composition()
    {
        using var folder = new TempFolder();
        var png = TestMedia.SolidPng(folder.File("media/extra.png"), 200, 400, (200, 30, 30));
        var other = TestMedia.SolidPng(folder.File("media/otro.png"), 200, 400, (30, 30, 200));
        var extraAsset = Guid.NewGuid();
        var otherAsset = Guid.NewGuid();
        var paths = new Dictionary<Guid, string> { [extraAsset] = png, [otherAsset] = other };
        var blocks = new[]
        {
            Block(0, ScriptBlockKind.CharacterShow, asset: extraAsset),
            Block(1, ScriptBlockKind.CharacterShow, asset: otherAsset),
            Block(2, ScriptBlockKind.Pause, pause: 1500),
            Block(3, ScriptBlockKind.CharacterHide, asset: extraAsset),
            Block(4, ScriptBlockKind.Pause, pause: 1000)
        };
        var scene = await SceneComposer.PlanAsync(blocks, b => b.AssetId is Guid id ? paths[id] : null);
        var hide = scene.Media.Single(x => x.Kind == ScriptBlockKind.CharacterHide);
        Assert.Equal((Guid?)blocks[0].Id, hide.HideBlockId, "el «Ocultar» apunta al primer render");
        var clips = VegasBridge.BuildClips(scene).Where(x => x.Kind == nameof(ScriptBlockKind.CharacterShow)).ToArray();
        var extra = clips.Single(x => x.BlockId == blocks[0].Id);
        var stays = clips.Single(x => x.BlockId == blocks[1].Id);
        Assert.Equal(1500L, extra.StartMs + extra.DurationMs, "el extra termina al ocultarse");
        Assert.Equal(scene.DurationMs, stays.StartMs + stays.DurationMs, "el otro NPC sigue hasta el final");
    }

    [Test("Director: [MOSTRAR] NPC | render y [OCULTAR] NPC | render; «Copiar escena» escribe lo mismo")]
    public static void DirectorLanguage()
    {
        var show = DirectorScript.ParseDirectorPrompt("[MOSTRAR] NPC | guardia | derecha")[0];
        Assert.Equal(null, show.Error, "mostrar sin error");
        Assert.Equal(("NPC", "guardia"), (show.CharacterName, show.ResourceQuery), "personaje NPC y render");
        var hide = DirectorScript.ParseDirectorPrompt("[OCULTAR] NPC | guardia")[0];
        Assert.Equal(null, hide.Error, "ocultar con render sin error");
        Assert.Equal((ScriptBlockKind.CharacterHide, "NPC", "guardia"), (hide.Kind, hide.CharacterName, hide.ResourceQuery), "ocultar NPC");
        var character = DirectorScript.ParseDirectorPrompt("[OCULTAR] Bart")[0];
        Assert.Equal((null as string, "Bart", ""), (character.Error, character.CharacterName, character.ResourceQuery), "ocultar personaje como antes");
        Assert.True(DirectorScript.ParseDirectorPrompt("[OCULTAR] NPC | guardia | otro")[0].Error is not null, "un solo render");
        Assert.True(DirectorScript.ParseDirectorPrompt("[OCULTAR] Bart | guardia")[0].Error is not null, "un personaje no lleva render");
        Assert.Equal("guardia", DirectorScript.ParseDirectorPrompt("[OCULTAR] NPC | medio cuerpo | guardia")[0].ResourceQuery,
            "un encuadre no es el render");
        var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { ["A7"] = Guid.NewGuid() };
        Assert.Equal("[OCULTAR] NPC | " + ids["A7"].ToString("D"), DirectorScript.ResolveAiAssetAliases("[OCULTAR] NPC | A7", ids),
            "la IA usa referencias A7: se resuelven también al ocultar");
    }

    [Test("IA: renders NPC → «mostrar» con NPC y «ocultar_npc»; no son objetivo de cámara ni gesto")]
    public static void AiSchema()
    {
        var context = new DirectorSchemaContext(["A1"], [], [], [], [],
            new Dictionary<string, IReadOnlyList<string>> { ["Bart"] = ["A2"], [DirectorAiSchema.NpcKey] = ["A3", "A4"] },
            ["Bart"], ["Bart"], [], false);
        foreach (var compact in new[] { false, true })
        {
            var json = JsonSerializer.Serialize(DirectorAiSchema.Build(context, compact));
            using var document = JsonDocument.Parse(json);
            var branches = document.RootElement.GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("anyOf")
                .EnumerateArray().ToArray();
            string Name(JsonElement x) => x.GetProperty("properties").GetProperty("accion").GetProperty("enum")[0].GetString()!;
            Assert.True(branches.Any(x => Name(x) == "ocultar_npc"), $"ocultar_npc (compacto={compact})");
            var camera = branches.First(x => Name(x) == "camara").GetProperty("properties").GetProperty("objetivo").GetProperty("enum")
                .EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.False(camera.Contains(DirectorAiSchema.NpcKey), "NPC no es objetivo de cámara");
        }
        using var hide = JsonDocument.Parse("""{"accion":"ocultar_npc","recurso":"A3"}""");
        Assert.Equal(("[OCULTAR] NPC | A3", ""), DirectorAiSchema.ToLine(hide.RootElement), "línea");
        Assert.Equal(null, DirectorAiSchema.CheckRefs(hide.RootElement, context), "render NPC ofrecido");
        using var wrong = JsonDocument.Parse("""{"accion":"ocultar_npc","recurso":"A2"}""");
        Assert.True(DirectorAiSchema.CheckRefs(wrong.RootElement, context) is not null, "un render de Bart no es NPC");
        using var show = JsonDocument.Parse("""{"accion":"mostrar","personaje":"NPC","recurso":"A4","posicion":"derecha","encuadre":"auto","transicion":"heredar","animar_x":0,"animar_y":0,"animar_ms":0}""");
        Assert.Equal(null, DirectorAiSchema.CheckRefs(show.RootElement, context), "mostrar NPC");
        Assert.True(DirectorAiSchema.ToLine(show.RootElement).Line.StartsWith("[MOSTRAR] NPC | A4"), "línea mostrar NPC");
    }
}

using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>«Aplicar al guion» (1.4.4): what happens with each mode when the scene is empty, unchanged or changed.</summary>
internal static class DraftApplyTests
{
    private static SceneScriptBlock Block(Guid scene, int order, string text, bool take = false) =>
        new(Guid.NewGuid(), scene, order, ScriptBlockKind.Dialogue, Text: text, GeneratedAudioHash: take ? "take" : null);

    private static bool Take(SceneScriptBlock block) => block.GeneratedAudioHash == "take";

    [Test("Aplicar borrador: escena vacía, sin cambios o cambiada, en continuación, «Sustituir» y otra escena")]
    public static void Decide()
    {
        var scene = Guid.NewGuid();
        SceneScriptBlock[] original = [Block(scene, 0, "a"), Block(scene, 1, "b"), Block(scene, 2, "c")];
        SceneScriptBlock[] changed = [original[0], original[2] with { OrderIndex = 1 }];
        SceneScriptBlock[] draft = [Block(scene, 0, "d1"), Block(scene, 1, "d2"), Block(scene, 2, "d3"), Block(scene, 3, "d4")];
        DraftApplyDecision Same(IReadOnlyList<SceneScriptBlock> current, bool replace = false) =>
            DraftApplyPolicy.Decide(current, original, draft, true, false, replace);

        // Empty scene: the draft fills it, whatever the mode.
        foreach (var (recorded, replace) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            var empty = DraftApplyPolicy.Decide([], original, draft, true, recorded, replace);
            Assert.Equal((DraftApplyAction.Apply, 4), (empty.Action, empty.Blocks.Count), $"escena vacía (voces={recorded}, sustituir={replace})");
        }

        // Unchanged scene: the continuation is added at the end.
        var same = Same(original);
        Assert.Equal((DraftApplyAction.Apply, false, 7), (same.Action, same.Replace, same.Blocks.Count), "continuación: se agrega");
        Assert.Equal(original[0].Id, same.Blocks[0].Id, "lo que había queda delante");
        var replaceSame = Same(original, replace: true);
        Assert.Equal((DraftApplyAction.ConfirmReplace, true, 4), (replaceSame.Action, replaceSame.Replace, replaceSame.Blocks.Count),
            "«Sustituir» siempre pregunta, aunque la escena no haya cambiado");
        Assert.Contains("3 bloques", replaceSame.Message, "dice cuánto se pierde");

        // Changed scene (blocks deleted after the draft was made).
        var append = Same(changed);
        Assert.Equal((DraftApplyAction.ConfirmAppend, false, 6), (append.Action, append.Replace, append.Blocks.Count), "continuación: avisa y agrega");
        Assert.Contains("ahora tiene 2 bloques", append.Message, "bloques actuales");
        Assert.Contains("Se agregarán 4 bloques", append.Message, "bloques que se agregan");
        Assert.Equal((DraftApplyAction.ConfirmReplace, true), (Same(changed, replace: true).Action, Same(changed, replace: true).Replace),
            "«Sustituir»: pregunta y sustituye");
        var edited = Same([original[0], original[1] with { Text = "otro" }, original[2]]);
        Assert.Equal(DraftApplyAction.ConfirmAppend, edited.Action, "un bloque editado también cuenta como cambio");

        // Another scene: added with a notice, replaced after asking, filled when empty.
        SceneScriptBlock[] other = [Block(Guid.NewGuid(), 0, "x")];
        var otherAppend = DraftApplyPolicy.Decide(other, original, draft, false, false, false, "Playa");
        Assert.Equal((DraftApplyAction.ConfirmAppend, 5), (otherAppend.Action, otherAppend.Blocks.Count), "otra escena: avisa y agrega");
        Assert.Contains("Se agregarán 4 bloques al final de «Playa»", otherAppend.Message, "nombra la escena");
        var otherReplace = DraftApplyPolicy.Decide(other, original, draft, false, false, true, "Playa");
        Assert.Equal((DraftApplyAction.ConfirmReplace, true), (otherReplace.Action, otherReplace.Replace), "otra escena: sustituir pregunta");
        Assert.Contains("«Playa» ya tiene 1 bloques", otherReplace.Message, "nombra la escena");
        Assert.Equal(DraftApplyAction.Apply, DraftApplyPolicy.Decide([], original, draft, false, true, false).Action, "otra escena vacía");

        Assert.Equal(DraftApplyAction.Refuse, DraftApplyPolicy.Decide(original, original, [], true, false, false).Action, "borrador vacío");
    }

    [Test("Aplicar borrador de voces: manda el borrador; lo quitado vuelve y lo agregado después va al final")]
    public static void Voices()
    {
        var scene = Guid.NewGuid();
        // The recorded lines passed to the scene, then the AI draft: the same lines (kept) with visuals around them.
        SceneScriptBlock[] original = [Block(scene, 0, "hola", true), Block(scene, 1, "adiós", true), Block(scene, 2, "fin", true)];
        SceneScriptBlock[] draft =
        [
            new(Guid.NewGuid(), scene, 0, ScriptBlockKind.Background), original[0] with { OrderIndex = 1 },
            new(Guid.NewGuid(), scene, 2, ScriptBlockKind.CharacterShow), original[1] with { OrderIndex = 3 }, original[2] with { OrderIndex = 4 }
        ];
        DraftApplyDecision Voices(IReadOnlyList<SceneScriptBlock> current) =>
            DraftApplyPolicy.Decide(current, original, draft, true, true, true, isRecordedTake: Take);

        // First load / unchanged scene: the draft replaces everything without asking.
        var first = Voices(original);
        Assert.Equal((DraftApplyAction.Apply, true), (first.Action, first.Replace), "primer cargue: sustituye sin preguntar");
        Assert.Sequence(draft.Select(x => x.Id), first.Blocks.Select(x => x.Id), "queda el borrador tal cual");

        // A line deleted after the draft was made comes back: the draft was built from it and its WAV is on disk.
        var deleted = Voices([original[0], original[2] with { OrderIndex = 1 }]);
        Assert.Equal((DraftApplyAction.ConfirmReplace, true), (deleted.Action, deleted.Replace), "escena cambiada: pregunta");
        Assert.Sequence(draft.Select(x => x.Id), deleted.Blocks.Select(x => x.Id), "lo quitado vuelve en su sitio");
        Assert.Contains("vuelven 1 bloque(s)", deleted.Message, "avisa de lo que vuelve");
        Assert.Equal(0, deleted.LostTakes, "no se pierde ninguna grabación");

        // Blocks added by hand after the draft go after it, in their order; an edited line takes the draft's version.
        var sfx = new SceneScriptBlock(Guid.NewGuid(), scene, 3, ScriptBlockKind.SoundEffect);
        var music = new SceneScriptBlock(Guid.NewGuid(), scene, 4, ScriptBlockKind.Music);
        var added = Voices([original[0] with { Text = "hola!!" }, original[1], original[2], sfx, music]);
        Assert.Sequence(draft.Select(x => x.Id).Append(sfx.Id).Append(music.Id), added.Blocks.Select(x => x.Id),
            "borrador primero, lo agregado a la cola");
        Assert.Contains("los 2 bloque(s) que agregaste después irán al final", added.Message, "avisa de lo agregado");
        Assert.Contains("1 bloque(s) que editaste quedan como en el borrador", added.Message, "avisa de lo editado");
        Assert.Equal("hola", added.Blocks.First(x => x.Id == original[0].Id).Text, "manda el borrador");

        // Recorded lines added to the scene after the draft are kept too (never lost silently).
        var newTake = Block(scene, 3, "extra", true);
        var withTake = Voices([.. original, newTake]);
        Assert.Equal((newTake.Id, 0), (withTake.Blocks[^1].Id, withTake.LostTakes), "una grabación agregada después no se pierde");

        // «Sustituir» by hand over another scene counts the recordings it would drop.
        var otherScene = DraftApplyPolicy.Decide([Block(Guid.NewGuid(), 0, "x", true)], original, draft, false, false, true, isRecordedTake: Take);
        Assert.Equal(1, otherScene.LostTakes, "cuenta las grabaciones que se dejan de usar");
    }
}

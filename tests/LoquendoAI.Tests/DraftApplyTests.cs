using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>«Aplicar al guion» (1.4.4): what happens with each mode when the scene is empty, unchanged or changed.</summary>
internal static class DraftApplyTests
{
    private static SceneScriptBlock Block(Guid scene, int order, string text) =>
        new(Guid.NewGuid(), scene, order, ScriptBlockKind.Dialogue, Text: text);

    [Test("Aplicar borrador: escena vacía, sin cambios o cambiada, en continuación, «Sustituir» y voces grabadas")]
    public static void Decide()
    {
        var scene = Guid.NewGuid();
        SceneScriptBlock[] original = [Block(scene, 0, "a"), Block(scene, 1, "b"), Block(scene, 2, "c")];
        SceneScriptBlock[] changed = [original[0], original[2] with { OrderIndex = 1 }];
        DraftApplyDecision Same(IReadOnlyList<SceneScriptBlock> current, bool recorded = false, bool replace = false, int draft = 4) =>
            DraftApplyPolicy.Decide(current, original, draft, true, recorded, replace);

        // Empty scene: the draft fills it, whatever the mode.
        foreach (var (recorded, replace) in new[] { (false, false), (false, true), (true, false), (true, true) })
            Assert.Equal((DraftApplyAction.Apply, false), (Same([], recorded, replace).Action, Same([], recorded, replace).Replace),
                $"escena vacía (voces={recorded}, sustituir={replace})");

        // Unchanged scene: as always.
        Assert.Equal((DraftApplyAction.Apply, false), (Same(original).Action, Same(original).Replace), "continuación: se agrega");
        Assert.Equal((DraftApplyAction.Apply, true), (Same(original, recorded: true).Action, Same(original, recorded: true).Replace),
            "voces: el borrador sustituye la escena");
        var replaceSame = Same(original, replace: true);
        Assert.Equal((DraftApplyAction.ConfirmReplace, true), (replaceSame.Action, replaceSame.Replace), "«Sustituir» siempre pregunta");
        Assert.Contains("3 bloques", replaceSame.Message, "dice cuánto se pierde");

        // Changed scene (blocks deleted after the draft was made).
        var append = Same(changed);
        Assert.Equal((DraftApplyAction.ConfirmAppend, false), (append.Action, append.Replace), "continuación: avisa y agrega");
        Assert.Contains("ahora tiene 2 bloques", append.Message, "bloques actuales");
        Assert.Contains("Se agregarán 4 bloques", append.Message, "bloques que se agregan");
        Assert.Equal((DraftApplyAction.ConfirmReplace, true), (Same(changed, replace: true).Action, Same(changed, replace: true).Replace),
            "«Sustituir»: pregunta y sustituye");
        var voices = Same(changed, recorded: true);
        Assert.Equal((DraftApplyAction.Refuse, DraftApplyPolicy.ChangedMessage), (voices.Action, voices.Message),
            "voces sobre una escena cambiada: el error de siempre");
        var edited = Same([original[0], original[1] with { Text = "otro" }, original[2]]);
        Assert.Equal(DraftApplyAction.ConfirmAppend, edited.Action, "un bloque editado también cuenta como cambio");

        // Another scene.
        SceneScriptBlock[] other = [Block(Guid.NewGuid(), 0, "x")];
        Assert.Equal(DraftApplyAction.Apply, DraftApplyPolicy.Decide(other, original, 4, false, false, false).Action, "otra escena: se agrega");
        var otherReplace = DraftApplyPolicy.Decide(other, original, 4, false, false, true, "Playa");
        Assert.Equal((DraftApplyAction.ConfirmReplace, true), (otherReplace.Action, otherReplace.Replace), "otra escena: sustituir pregunta");
        Assert.Contains("«Playa» ya tiene 1 bloques", otherReplace.Message, "nombra la escena");
        Assert.Equal(DraftApplyAction.Apply, DraftApplyPolicy.Decide([], original, 4, false, true, false).Action, "otra escena vacía");

        Assert.Equal(DraftApplyAction.Refuse, Same(original, draft: 0).Action, "borrador vacío");
    }
}

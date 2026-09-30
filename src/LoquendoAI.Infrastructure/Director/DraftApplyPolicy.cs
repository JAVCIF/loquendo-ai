using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Director;

public enum DraftApplyAction
{
    /// <summary>Apply without asking.</summary>
    Apply,
    /// <summary>Ask first; the draft is added after the current blocks.</summary>
    ConfirmAppend,
    /// <summary>Ask first; the draft takes the place of the current blocks (see <see cref="DraftApplyDecision.Blocks"/>).</summary>
    ConfirmReplace,
    /// <summary>Do not apply; show <see cref="DraftApplyDecision.Message"/>.</summary>
    Refuse
}

/// <summary>What «Aplicar al guion» does with a draft. <see cref="Blocks"/> is the resulting scene, in order and before
/// renumbering (empty when refused). <see cref="Replace"/>: the draft takes the place of the current blocks instead of
/// being added after them. <see cref="Message"/> is the question or the reason.</summary>
public sealed record DraftApplyDecision(DraftApplyAction Action, bool Replace, string Message, IReadOnlyList<SceneScriptBlock> Blocks)
{
    /// <summary>Recorded takes of the current scene that the result no longer uses (the WAVs stay on disk).</summary>
    public int LostTakes { get; init; }
}

/// <summary>
/// The rules of «Aplicar al guion» (1.4.4), kept apart from the window so every case is tested:
/// <list type="bullet">
/// <item>An empty scene takes the draft directly, whatever the mode: there is nothing to lose.</item>
/// <item>An unchanged scene works as always: a continuation is added at the end; the voices draft (the whole scene,
/// recorded WAVs included) replaces it; «Sustituir» asks first.</item>
/// <item>A continuation over a scene that changed since the draft was made says how many blocks it adds and asks.</item>
/// <item>A voices draft over a changed scene is still the law: it replaces the scene as it was when it was made (what was
/// deleted since comes back, it was built from those lines and the WAVs are still on disk), and the blocks added by hand
/// since go after it, in their order. It asks first, saying so.</item>
/// <item>«Sustituir» (checked by hand) always asks, and replaces everything.</item>
/// <item>Another scene: an empty one takes the draft; otherwise adding says how many blocks and replacing asks.</item>
/// </list>
/// </summary>
public static class DraftApplyPolicy
{
    public static DraftApplyDecision Decide(IReadOnlyList<SceneScriptBlock> current, IReadOnlyList<SceneScriptBlock> original,
        IReadOnlyList<SceneScriptBlock> draft, bool sameScene, bool recordedDraft, bool replaceChecked, string sceneTitle = "",
        Func<SceneScriptBlock, bool>? isRecordedTake = null)
    {
        if (draft.Count == 0) return new(DraftApplyAction.Refuse, false, "El borrador está vacío.", []);
        var name = sceneTitle.Length > 0 ? $"«{sceneTitle}»" : "la escena";
        DraftApplyDecision Append(DraftApplyAction action, string message) =>
            new(action, false, message, current.Concat(draft).ToArray());
        DraftApplyDecision Replace(DraftApplyAction action, string message, IReadOnlyList<SceneScriptBlock> blocks) =>
            new(action, true, message, blocks) { LostTakes = LostTakes(current, blocks, isRecordedTake) };

        if (current.Count == 0) return Append(DraftApplyAction.Apply, "");
        if (!sameScene)
            return recordedDraft || replaceChecked
                ? Replace(DraftApplyAction.ConfirmReplace,
                    $"{Capital(name)} ya tiene {current.Count} bloques. ¿Sustituirlos por el borrador ({draft.Count} bloques)?", draft)
                : Append(DraftApplyAction.ConfirmAppend,
                    $"Se agregarán {draft.Count} bloques al final de {name} (ahora tiene {current.Count}). ¿Continuar?");

        var changed = !current.SequenceEqual(original);
        if (replaceChecked && !recordedDraft)
            return Replace(DraftApplyAction.ConfirmReplace,
                $"¿Seguro? Se perderá la escena actual ({current.Count} bloques) y quedará solo el borrador ({draft.Count} bloques).", draft);
        if (recordedDraft)
        {
            if (!changed) return Replace(DraftApplyAction.Apply, "", draft);
            var originalIds = original.Select(x => x.Id).ToHashSet();
            var draftIds = draft.Select(x => x.Id).ToHashSet();
            var currentIds = current.Select(x => x.Id).ToHashSet();
            var added = current.Where(x => !originalIds.Contains(x.Id) && !draftIds.Contains(x.Id)).ToArray();
            var restored = original.Count(x => !currentIds.Contains(x.Id) && draftIds.Contains(x.Id));
            var edited = current.Count(x => originalIds.Contains(x.Id) && !original.Contains(x));
            var parts = new List<string>();
            if (restored > 0) parts.Add($"vuelven {restored} bloque(s) que habías quitado");
            if (edited > 0) parts.Add($"{edited} bloque(s) que editaste quedan como en el borrador");
            if (added.Length > 0) parts.Add($"los {added.Length} bloque(s) que agregaste después irán al final");
            return Replace(DraftApplyAction.ConfirmReplace,
                "El guion cambió desde que se generó el borrador. Se aplicará el borrador de voces" +
                (parts.Count > 0 ? ": " + string.Join("; ", parts) : "") + ". ¿Continuar?", draft.Concat(added).ToArray());
        }
        return changed
            ? Append(DraftApplyAction.ConfirmAppend,
                $"El guion cambió desde que se creó el borrador (ahora tiene {current.Count} bloques). " +
                $"Se agregarán {draft.Count} bloques al final. ¿Continuar?")
            : Append(DraftApplyAction.Apply, "");
    }

    private static int LostTakes(IReadOnlyList<SceneScriptBlock> current, IReadOnlyList<SceneScriptBlock> result,
        Func<SceneScriptBlock, bool>? isRecordedTake)
    {
        if (isRecordedTake is null) return 0;
        var kept = result.Select(x => x.Id).ToHashSet();
        return current.Count(x => isRecordedTake(x) && !kept.Contains(x.Id));
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

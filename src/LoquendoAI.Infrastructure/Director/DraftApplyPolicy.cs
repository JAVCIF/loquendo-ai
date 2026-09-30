using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Director;

public enum DraftApplyAction
{
    /// <summary>Apply without asking.</summary>
    Apply,
    /// <summary>Ask first; the draft is added after the current blocks.</summary>
    ConfirmAppend,
    /// <summary>Ask first; the draft replaces the current blocks.</summary>
    ConfirmReplace,
    /// <summary>Do not apply; show <see cref="DraftApplyDecision.Message"/>.</summary>
    Refuse
}

/// <summary>What «Aplicar al guion» does with a draft. <see cref="Replace"/>: the draft takes the place of the
/// current blocks (otherwise it is added after them). <see cref="Message"/> is the question or the reason.</summary>
public sealed record DraftApplyDecision(DraftApplyAction Action, bool Replace, string Message);

/// <summary>
/// The rules of «Aplicar al guion» (1.4.4), kept apart from the window so every case is tested:
/// <list type="bullet">
/// <item>An empty scene takes the draft directly, whatever the mode: there is nothing to lose.</item>
/// <item>An unchanged scene works as always: a continuation is added at the end; «Sustituir» asks first; the voices
/// draft (the whole scene, recorded WAVs included) replaces it.</item>
/// <item>A scene that changed since the draft was made: a continuation says how many blocks will be added and asks;
/// «Sustituir» asks and replaces; the voices draft is refused (it was built from the blocks and WAVs that changed).</item>
/// <item>Another scene: a continuation is added; replacing its blocks asks first.</item>
/// </list>
/// </summary>
public static class DraftApplyPolicy
{
    public const string ChangedMessage =
        "El guion original cambió desde que se creó el borrador. Elige otra escena o copia el borrador para conservarlo.";

    public static DraftApplyDecision Decide(IReadOnlyList<SceneScriptBlock> current, IReadOnlyList<SceneScriptBlock> original,
        int draftCount, bool sameScene, bool recordedDraft, bool replaceChecked, string sceneTitle = "")
    {
        if (draftCount == 0) return new(DraftApplyAction.Refuse, false, "El borrador está vacío.");
        var replace = recordedDraft || replaceChecked;
        if (current.Count == 0)
            return new(DraftApplyAction.Apply, false, "");
        var name = sceneTitle.Length > 0 ? $"«{sceneTitle}»" : "La escena";
        if (!sameScene)
            return replace
                ? new(DraftApplyAction.ConfirmReplace, true, $"{name} ya tiene {current.Count} bloques. ¿Sustituirlos por el borrador?")
                : new(DraftApplyAction.Apply, false, "");
        var changed = !current.SequenceEqual(original);
        if (recordedDraft)
            return changed ? new(DraftApplyAction.Refuse, true, ChangedMessage) : new(DraftApplyAction.Apply, true, "");
        if (replaceChecked)
            return new(DraftApplyAction.ConfirmReplace, true,
                $"¿Seguro? Se perderá la escena actual ({current.Count} bloques) y quedará solo el borrador ({draftCount} bloques).");
        return changed
            ? new(DraftApplyAction.ConfirmAppend, false,
                $"El guion cambió desde que se creó el borrador (ahora tiene {current.Count} bloques). " +
                $"Se agregarán {draftCount} bloques al final. ¿Continuar?")
            : new(DraftApplyAction.Apply, false, "");
    }
}

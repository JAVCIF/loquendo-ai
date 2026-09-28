using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Persistence;

namespace LoquendoAI.App;

/// <summary>Cinematic bars ([CINE]): character names in «capas=» and continuity between scenes.</summary>
public partial class MainWindow
{
    /// <summary>Registered character by name (accent- and case-insensitive).</summary>
    private Guid? CharacterIdByName(string name) => _characters.FirstOrDefault(x => SameDirectorName(x.Name, name))?.Id;

    /// <summary>Error for «[CINE] … | capas=Bart,Nadie» when a name is not a registered character.</summary>
    private string? CinemaNamesIssue(DirectorSpec spec)
    {
        if (spec.Kind != ScriptBlockKind.Cinema) return null;
        var (_, names) = CinemaLayers(spec.Options?.GetValueOrDefault("CAPAS"));
        var unknown = names.Where(name => CharacterIdByName(name) is null).ToArray();
        return unknown.Length == 0 ? null : "Personaje sin registrar en capas: " + string.Join(", ", unknown);
    }

    /// <summary>Bars a scene starts with: how the previous scenes of its episode leave them
    /// (a single [CINE] mostrar in the first scene keeps the whole episode «in cinema»).</summary>
    private async Task<CinemaState?> CinemaAtSceneStartAsync(SqliteProjectRepository repository, Guid sceneId)
    {
        CinemaState? state = null;
        foreach (var scene in _scenes.OrderBy(x => x.Index))
        {
            if (scene.Id == sceneId) break;
            state = CinemaPlan.EndState(await repository.GetSceneScriptBlocksAsync(scene.Id), state);
        }
        return state;
    }
}

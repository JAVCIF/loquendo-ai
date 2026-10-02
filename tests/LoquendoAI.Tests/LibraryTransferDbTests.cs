using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Projects;

namespace LoquendoAI.Tests;

/// <summary>Importing the library of another project (1.4.8) with two real projects: the other one stays open and
/// untouched, nothing is duplicated, the search index sees what arrived and importing again changes nothing.</summary>
internal static class LibraryTransferDbTests
{
    [Test("Importar de otro proyecto (SQLite): biblioteca, perfiles y personajes; el otro sigue abierto e intacto; la búsqueda lo encuentra")]
    public static async Task ImportBetweenProjects()
    {
        using var folder = new TempFolder();
        var renders = folder.File("renders");
        Directory.CreateDirectory(renders);
        var now = DateTimeOffset.UtcNow;
        var service = new ProjectService();

        await using var main = await service.CreateAsync(folder.File("principal"), "Principal");
        var source = new AssetSource(Guid.NewGuid(), "Renders", renders, true, now, now);
        await main.UpsertAssetSourceAsync(source);
        await main.ReplaceFolderRulesAsync(source.Id, [new FolderRule(Guid.NewGuid(), source.Id, "BART", FolderClassification.CharacterRenders, "Bart", null)]);
        AssetRecord Asset(Guid sourceId, string path, string? subject) => new(Guid.NewGuid(), $"@source/{sourceId:D}/{path}",
            AssetKind.CharacterSprite, "sha-" + path, Path.GetFileNameWithoutExtension(path), now, sourceId, path, 10, now, ".png",
            CutoutStatus.Unknown, subject);
        var bart = Asset(source.Id, "BART/feliz.png", "Bart");
        var lisa = Asset(source.Id, "LISA/triste.png", "Lisa");
        await main.UpsertAssetAsync(bart);
        await main.UpsertAssetAsync(lisa);
        await main.ReplaceDirectorAssetTagsAsync(lisa.Id, "director.auto.", [new AssetTag(lisa.Id, "director.auto.description", "saxofonista melancolica")]);
        var jorge = new VoiceProfile(Guid.NewGuid(), "Jorge grave", "loquendo", "Jorge", 40);
        await main.UpsertVoiceProfileAsync(jorge);
        await main.UpsertCharacterAsync(new CharacterDefinition(Guid.NewGuid(), "Bart", jorge.Id));

        // The new project already has the same folder (another id) with Bart's render used in a scene.
        await using var other = await service.CreateAsync(folder.File("nuevo"), "Nuevo");
        var here = new AssetSource(Guid.NewGuid(), "Mis renders", renders + Path.DirectorySeparatorChar, true, now, null);
        await other.UpsertAssetSourceAsync(here);
        var bartHere = Asset(here.Id, "BART/feliz.png", null);
        await other.UpsertAssetAsync(bartHere);
        var mainBefore = File.GetLastWriteTimeUtc(Path.Combine(main.ProjectRoot, ProjectLayout.DatabaseFileName));

        var plan = await LibraryTransfer.ImportAsync(main.ProjectRoot, other, new LibraryTransferOptions(true, true, true));
        Assert.Equal((0, 1, 1, 1, 1), (plan.NewSources, plan.NewAssets, plan.UpdatedAssets, plan.NewProfiles, plan.NewCharacters), "conteo");
        var sources = await other.GetAssetSourcesAsync();
        Assert.Equal(here.Id, sources.Single().Id, "la misma carpeta no se duplica");
        var assets = await other.GetAssetsAsync();
        Assert.Equal(2, assets.Count, "Bart (el que ya estaba) y Lisa");
        Assert.Equal((bartHere.Id, "Bart"), (assets.Single(x => x.Id == bartHere.Id).Id, assets.Single(x => x.Id == bartHere.Id).SubjectName),
            "Bart conserva su id y toma la clasificación");
        Assert.Equal("Bart", (await other.GetFolderRulesAsync(here.Id)).Single().SubjectName, "regla de la carpeta");
        Assert.Equal("Jorge grave", (await other.GetVoiceProfilesAsync()).Single(x => x.Name == "Jorge grave").Name, "perfil");
        Assert.Equal(1, (await other.GetCharactersAsync()).Count(x => x.Name == "Bart" && x.DefaultVoiceProfileId == jorge.Id), "personaje con su voz");
        var found = await other.SearchAssetIdsAsync("saxofonista");
        Assert.True(found is null || found.Contains(assets.Single(x => x.SubjectName == "Lisa").Id), "la búsqueda encuentra lo importado por sus etiquetas");

        var again = await LibraryTransfer.ImportAsync(main.ProjectRoot, other, new LibraryTransferOptions(true, true, true));
        Assert.True(again.IsEmpty, "importar otra vez no cambia nada");
        Assert.Equal(2, (await main.GetAssetsAsync()).Count, "el proyecto principal sigue abierto y con lo suyo");
        Assert.Equal(mainBefore, File.GetLastWriteTimeUtc(Path.Combine(main.ProjectRoot, ProjectLayout.DatabaseFileName)), "y sin tocar");
    }
}

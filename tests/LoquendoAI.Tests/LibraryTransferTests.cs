using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Projects;

namespace LoquendoAI.Tests;

/// <summary>Importing the library of another project (1.4.8): the merge, without a database.</summary>
internal static class LibraryTransferTests
{
    private static readonly DateTimeOffset Then = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static AssetSource Source(string path, Guid? id = null) => new(id ?? Guid.NewGuid(), "Renders", path, true, Then, Then);

    private static AssetRecord Asset(AssetSource source, string path, string? subject = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), $"@source/{source.Id:D}/{path}", AssetKind.CharacterSprite, "sha-" + path, Path.GetFileNameWithoutExtension(path), Then,
            source.Id, path, 100, Then, ".png", CutoutStatus.Unknown, subject);

    private static LibrarySnapshot Snapshot(AssetSource[]? sources = null, FolderRule[]? rules = null, AssetRecord[]? assets = null,
        AssetTag[]? tags = null, VoiceProfile[]? profiles = null, CharacterDefinition[]? characters = null) =>
        new(sources ?? [], rules ?? [], assets ?? [], tags ?? [], profiles ?? [], characters ?? []);

    [Test("Importar biblioteca: a un proyecto vacío pasa todo tal cual (menos los archivos propios del proyecto)")]
    public static void IntoEmpty()
    {
        var source = Source(@"D:\Loquendo\Renders");
        var bart = Asset(source, "BART/feliz.png", "Bart");
        var voice = new AssetRecord(Guid.NewGuid(), "generated/imported_voices/x.wav", AssetKind.SoundEffect, "h", "x", Then);
        var rule = new FolderRule(Guid.NewGuid(), source.Id, "BART", FolderClassification.CharacterRenders, "Bart", null);
        var tag = new AssetTag(bart.Id, "director.auto.description", "Bart sonriendo");
        var from = Snapshot([source], [rule], [bart, voice], [tag]);

        var plan = LibraryTransfer.Plan(from, Snapshot(), new LibraryTransferOptions());
        Assert.Equal((1, 1, 0), (plan.NewSources, plan.NewAssets, plan.UpdatedAssets), "una fuente y un recurso nuevos");
        Assert.Equal(source.Id, plan.Sources.Single().Id, "conserva el id de la fuente");
        Assert.Equal(bart, plan.Assets.Single(), "el recurso tal cual, con su clasificación");
        Assert.Equal(rule, plan.Rules[source.Id].Single(), "la regla de la carpeta");
        Assert.Equal(tag, plan.Tags[bart.Id].Single(), "sus etiquetas");
        Assert.True(plan.Profiles.Count == 0 && plan.Characters.Count == 0, "sin perfiles ni personajes si no se piden");
        var again = LibraryTransfer.Plan(from, Snapshot([source], [rule], [bart], [tag]), new LibraryTransferOptions());
        Assert.True(again.Assets.Count == 0 && again.Tags.Count == 0, "importar otra vez lo mismo (con etiquetas) no reescribe nada");
    }

    [Test("Importar biblioteca: la misma carpeta no se duplica; los recursos que ya están conservan su id y toman la clasificación importada")]
    public static void SameFolder()
    {
        var there = Source(@"D:\Loquendo\Renders\");
        var here = Source("d:/loquendo/renders");
        var bartThere = Asset(there, "BART/feliz.png", "Bart");
        var lisaThere = Asset(there, "LISA/triste.png", "Lisa");
        var bartHere = Asset(here, "bart/FELIZ.png", null); // the same file, written differently, without classification
        var rulesThere = new[]
        {
            new FolderRule(Guid.NewGuid(), there.Id, "BART", FolderClassification.CharacterRenders, "Bart", null),
            new FolderRule(Guid.NewGuid(), there.Id, "LISA", FolderClassification.CharacterRenders, "Lisa", null)
        };
        var ruleHere = new FolderRule(Guid.NewGuid(), here.Id, "bart/", FolderClassification.CharacterRenders, "Bartolo", null);
        var otherHere = new FolderRule(Guid.NewGuid(), here.Id, "FONDOS", FolderClassification.Backgrounds, null, "Casa");
        var plan = LibraryTransfer.Plan(Snapshot([there], rulesThere, [bartThere, lisaThere]),
            Snapshot([here], [ruleHere, otherHere], [bartHere]), new LibraryTransferOptions());

        Assert.Equal(0, plan.NewSources, "la misma carpeta (otra barra, mayúsculas y la barra final)");
        Assert.True(plan.Sources.All(x => x.Id == here.Id), "si se escribe, es la fuente de aquí");
        var bart = plan.Assets.Single(x => x.SourceRelativePath == "bart/FELIZ.png");
        Assert.Equal((bartHere.Id, here.Id, "Bart"), (bart.Id, bart.SourceId!.Value, bart.SubjectName), "mismo id (sus escenas siguen), clasificación importada");
        Assert.Equal($"@source/{here.Id:D}/bart/FELIZ.png", bart.RelativePath, "ruta interna con la fuente de aquí");
        var lisa = plan.Assets.Single(x => x.SubjectName == "Lisa");
        Assert.Equal((lisaThere.Id, here.Id), (lisa.Id, lisa.SourceId!.Value), "Lisa es nueva: entra en la fuente de aquí");
        Assert.Equal((1, 1), (plan.NewAssets, plan.UpdatedAssets), "una nueva, una actualizada");
        var rules = plan.Rules[here.Id];
        Assert.Equal((3, "Bart", ruleHere.Id), (rules.Count, rules.Single(x => x.RelativeFolder == "BART").SubjectName,
            rules.Single(x => x.RelativeFolder == "BART").Id), "la regla importada manda en su carpeta y conserva el id de aquí");
        Assert.True(rules.Any(x => x.Id == otherHere.Id), "las reglas de otras carpetas se quedan");

        // Importing again changes nothing.
        var after = Snapshot([here], rules.ToArray(), plan.Assets.ToArray());
        var again = LibraryTransfer.Plan(Snapshot([there], rulesThere, [bartThere, lisaThere]), after, new LibraryTransferOptions());
        Assert.Equal((0, 0, 0), (again.NewSources, again.NewAssets, again.UpdatedAssets), "segunda vez: nada nuevo");
        Assert.Equal(0, again.Assets.Count, "ni recursos que reescribir");
    }

    [Test("Importar biblioteca: un id que aquí ya es de otra cosa recibe uno nuevo")]
    public static void IdCollision()
    {
        var shared = Guid.NewGuid();
        var there = Source(@"D:\Renders", shared);
        var here = Source(@"E:\Otra", shared);
        var asset = Asset(there, "a.png");
        var plan = LibraryTransfer.Plan(Snapshot([there], assets: [asset]), Snapshot([here]), new LibraryTransferOptions());
        var source = plan.Sources.Single();
        Assert.True(source.Id != shared && source.RootPath == @"D:\Renders", "otra carpeta con el mismo id: fuente nueva con otro id");
        Assert.Equal(source.Id, plan.Assets.Single().SourceId!.Value, "sus recursos van a la fuente nueva");
    }

    [Test("Importar perfiles y personajes: por nombre, sin duplicar; un personaje sin voz recibe la suya")]
    public static void ProfilesAndCharacters()
    {
        var jorgeThere = new VoiceProfile(Guid.NewGuid(), "Jorge grave", "loquendo", "Jorge", 40);
        var carmenThere = new VoiceProfile(Guid.NewGuid(), "Carmen", "loquendo", "Carmen");
        var jorgeHere = new VoiceProfile(Guid.NewGuid(), "jorge grave ", "loquendo", "Jorge", 45);
        var bartThere = new CharacterDefinition(Guid.NewGuid(), "Bart", jorgeThere.Id);
        var lisaThere = new CharacterDefinition(Guid.NewGuid(), "Lisa", carmenThere.Id);
        var bartHere = new CharacterDefinition(Guid.NewGuid(), "BART", null);
        var from = Snapshot(profiles: [jorgeThere, carmenThere], characters: [bartThere, lisaThere]);
        var into = Snapshot(profiles: [jorgeHere], characters: [bartHere]);

        var plan = LibraryTransfer.Plan(from, into, new LibraryTransferOptions(Library: false, Profiles: true, Characters: true));
        Assert.Sequence(["Carmen"], plan.Profiles.Select(x => x.Name), "«Jorge grave» ya está: solo Carmen");
        Assert.Equal((bartHere.Id, (Guid?)jorgeHere.Id), (plan.Characters.Single(x => x.Name == "BART").Id,
            plan.Characters.Single(x => x.Name == "BART").DefaultVoiceProfileId), "Bart ya está: recibe la voz con el perfil de aquí");
        Assert.Equal((Guid?)carmenThere.Id, plan.Characters.Single(x => x.Name == "Lisa").DefaultVoiceProfileId, "Lisa nueva, con Carmen");
        Assert.Equal((1, 1), (plan.NewProfiles, plan.NewCharacters), "conteo");

        // Characters without their profiles: only voices that already exist here are kept.
        var onlyCharacters = LibraryTransfer.Plan(from, into, new LibraryTransferOptions(Library: false, Characters: true));
        Assert.Equal((0, (Guid?)null), (onlyCharacters.Profiles.Count, onlyCharacters.Characters.Single(x => x.Name == "Lisa").DefaultVoiceProfileId),
            "sin importar perfiles, Lisa queda sin voz");
        Assert.True(LibraryTransfer.Plan(from, into, new LibraryTransferOptions(Library: false)).IsEmpty, "nada pedido: nada");
    }
}

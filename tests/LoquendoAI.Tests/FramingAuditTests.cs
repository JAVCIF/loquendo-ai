using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.Tests;

/// <summary>«Revisar encuadre» (1.4.4): renders out of the frame after their animation, tiny characters and props,
/// and that each proposed fix really leaves the scene well framed.</summary>
internal static class FramingAuditTests
{
    private sealed class Scene(string character, string wide, string prop)
    {
        public readonly List<SceneScriptBlock> Blocks = [];
        public readonly Guid Bart = Guid.NewGuid(), Lisa = Guid.NewGuid();

        public SceneScriptBlock Show(Guid who, BlockParameters parameters, bool wideRender = false)
        {
            var block = new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, Blocks.Count, ScriptBlockKind.CharacterShow, who,
                AssetId: wideRender ? WideAsset : CharacterAsset, ParametersJson: parameters.ToJson());
            Blocks.Add(block);
            return block;
        }

        public SceneScriptBlock Prop(BlockParameters parameters)
        {
            var block = new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, Blocks.Count, ScriptBlockKind.Image,
                AssetId: PropAsset, ParametersJson: parameters.ToJson());
            Blocks.Add(block);
            return block;
        }

        public void Hide(Guid who) => Blocks.Add(new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, Blocks.Count, ScriptBlockKind.CharacterHide, who));
        public void Pause(int ms) => Blocks.Add(new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, Blocks.Count, ScriptBlockKind.Pause, PauseAfterMs: ms));

        private static readonly Guid CharacterAsset = Guid.NewGuid(), WideAsset = Guid.NewGuid(), PropAsset = Guid.NewGuid();

        public async Task<IReadOnlyList<FramingIssue>> AuditAsync()
        {
            var planned = await SceneComposer.PlanAsync(Blocks, b => b.AssetId == WideAsset ? wide : b.AssetId == PropAsset ? prop :
                b.AssetId == CharacterAsset ? character : null);
            return await FramingAudit.AuditAsync(Blocks, planned, b => b.CharacterId == Bart ? "Bart" : b.CharacterId == Lisa ? "Lisa" : "Prop");
        }

        /// <summary>Applies a fix to its block, as the app does.</summary>
        public void Fix(Guid blockId, BlockParameters changes)
        {
            var index = Blocks.FindIndex(x => x.Id == blockId);
            Blocks[index] = Blocks[index] with { ParametersJson = FramingAudit.Apply(BlockParameters.Of(Blocks[index]), changes).ToJson() };
        }

        public BlockParameters Of(Guid blockId) => BlockParameters.Of(Blocks.First(x => x.Id == blockId));
    }

    private static Scene NewScene(TempFolder folder) => new(
        Render(folder.File("media/pj.png"), 400, 800, (220, 20, 20)),
        Render(folder.File("media/sillon.png"), 900, 500, (20, 160, 20)),
        Render(folder.File("media/prop.png"), 300, 200, (20, 20, 220)));

    /// <summary>A render as they come: an opaque figure with a thin transparent edge (a plain opaque rectangle is now
    /// reported as an image without transparency).</summary>
    private static string Render(string path, int width, int height, (byte R, byte G, byte B) color) =>
        TestMedia.Png(path, width, height, (x, y) => x < 2 || y < 2 || x >= width - 2 || y >= height - 2
            ? ((byte)0, (byte)0, (byte)0, (byte)0) : (color.R, color.G, color.B, (byte)255));

    private static BlockParameters Character(string position, int moveX = 0, int offsetX = 0, bool mirror = false) => new()
    {
        Position = position, FramingPreset = "auto", MotionOffsetX = moveX, MotionDurationMs = 600, VisualOffsetX = offsetX,
        FlipHorizontal = mirror
    };

    [Test("Encuadre: animación que saca el render del cuadro → «Limitar» (conserva la dirección) o «Era una entrada» si es su aparición")]
    public static async Task OutOfFrame()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        var scene = NewScene(folder);
        // As in the user's scene: at the left and moving 200 px further left, on its first appearance.
        var bart = scene.Show(scene.Bart, Character("izquierda", -200));
        var lisa = scene.Show(scene.Lisa, Character("derecha", 200));
        scene.Pause(2000);
        var issues = await scene.AuditAsync();
        var bartIssue = issues.Single(x => x.BlockId == bart.Id);
        Assert.Equal(FramingProblem.OutOfFrame, bartIssue.Problem, "Bart sale del cuadro");
        Assert.Contains("Bart termina", bartIssue.Message, "mensaje");
        Assert.Sequence(["limitar", "entrada"], bartIssue.Fixes.Select(x => x.Id), "primera aparición: también «Era una entrada»");
        Assert.True(issues.Any(x => x.BlockId == lisa.Id && x.Problem == FramingProblem.OutOfFrame), "Lisa también (por la derecha)");

        var limit = bartIssue.Fixes[0].Changes;
        Assert.True(limit.MotionOffsetX is < 0 and > -200, $"limitar acorta el movimiento sin cambiar su dirección ({limit.MotionOffsetX})");
        var entrance = bartIssue.Fixes[1].Changes;
        Assert.Equal(((int?)-200, (int?)200), (entrance.VisualOffsetX, entrance.MotionOffsetX), "entrada: empieza fuera y entra hasta su sitio");

        scene.Fix(bart.Id, limit);
        scene.Fix(lisa.Id, issues.Single(x => x.BlockId == lisa.Id).Fixes.First(x => x.Id == "entrada").Changes);
        var after = await scene.AuditAsync();
        Assert.True(!after.Any(x => x.Problem == FramingProblem.OutOfFrame), "corregidos: nada fuera del cuadro");
        Assert.Equal(((int?)200, (int?)-200), (scene.Of(lisa.Id).VisualOffsetX, scene.Of(lisa.Id).MotionOffsetX), "Lisa entra desde la derecha");
    }

    [Test("Encuadre: un empujón en plena escena solo se limita; una salida antes de ocultarse no se toca; espejo e X fuera")]
    public static async Task PushExitMirrorAndOffset()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        var scene = NewScene(folder);
        scene.Show(scene.Bart, Character("izquierda"));
        scene.Pause(1000);
        var push = scene.Show(scene.Bart, Character("izquierda", -300)); // already on screen: a push, not an entrance
        scene.Pause(1000);
        var exit = scene.Show(scene.Lisa, Character("derecha", 500));
        scene.Pause(700);
        scene.Hide(scene.Lisa); // runs off and is hidden: an exit
        var mirrored = scene.Show(scene.Lisa, Character("izquierda", -250, mirror: true)); // «Invertir»: right side, moving right
        scene.Pause(1000);
        scene.Hide(scene.Bart);
        var offset = scene.Show(scene.Bart, Character("izquierda", offsetX: -420)); // the place itself is out
        scene.Pause(1000);
        var issues = await scene.AuditAsync();

        var pushIssue = issues.Single(x => x.BlockId == push.Id);
        Assert.Sequence(["limitar"], pushIssue.Fixes.Select(x => x.Id), "empujón: solo limitar");
        Assert.True(!issues.Any(x => x.BlockId == exit.Id), "salida antes de ocultarse: se respeta");
        var mirrorFix = issues.Single(x => x.BlockId == mirrored.Id).Fixes[0].Changes;
        Assert.True(mirrorFix.MotionOffsetX is < 0 and > -250, $"en el bloque invertido el valor sigue en su sentido ({mirrorFix.MotionOffsetX})");
        var offsetIssue = issues.Single(x => x.BlockId == offset.Id);
        Assert.Contains("queda", offsetIssue.Message, "sin animación: «queda» fuera");
        Assert.True(offsetIssue.Fixes[0].Changes.VisualOffsetX is > -420 and <= 0, "acerca el desplazamiento");

        foreach (var issue in issues) scene.Fix(issue.BlockId, issue.Fixes[0].Changes);
        Assert.True(!(await scene.AuditAsync()).Any(x => x.Problem == FramingProblem.OutOfFrame), "todo corregido");
    }

    [Test("Encuadre: la memoria por escena deja cambiar de «Limitar» a «Era una entrada», deshacer o marcar «a propósito»")]
    public static async Task Memory()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        var scene = NewScene(folder);
        var bart = scene.Show(scene.Bart, Character("izquierda", -200));
        var lisa = scene.Show(scene.Lisa, Character("derecha", 200));
        scene.Pause(2000);
        var issues = await scene.AuditAsync();
        var bartIssue = issues.Single(x => x.BlockId == bart.Id);
        var originalBart = scene.Blocks.First(x => x.Id == bart.Id);

        var memory = new FramingMemory();
        void Save(SceneScriptBlock block) => scene.Blocks[scene.Blocks.FindIndex(x => x.Id == block.Id)] = block;
        Save(memory.Choose(originalBart, bartIssue, "limitar"));
        Save(memory.Choose(scene.Blocks.First(x => x.Id == lisa.Id), issues.Single(x => x.BlockId == lisa.Id), FramingMemory.Intended));
        var path = folder.File("proyecto/generated/encuadre/escena.json");
        memory.Save(path);

        // Reopened: the corrected block is remembered and can switch to the entrance, computed from its original values.
        var again = FramingMemory.Load(path);
        again.Prune(scene.Blocks);
        Assert.Equal("limitar", again.Of(bart.Id)?.Choice, "recuerda la corrección");
        Assert.Equal(0, again.Pending(await scene.AuditAsync()).Count, "Lisa marcada «a propósito» ya no se avisa; Bart ya está bien");
        var entrance = again.Choose(scene.Blocks.First(x => x.Id == bart.Id), null, "entrada");
        Assert.Equal(((int?)-200, (int?)200), (BlockParameters.Of(entrance).VisualOffsetX, BlockParameters.Of(entrance).MotionOffsetX),
            "«Era una entrada» sale de los valores originales, no de los ya limitados");
        var undone = again.Choose(entrance, null, FramingMemory.Undone);
        Assert.Equal(originalBart.ParametersJson, undone.ParametersJson, "deshacer vuelve a lo que había");

        // A hand edit or another render: the record no longer applies and the block is reviewed from scratch.
        Save(entrance);
        var edited = scene.Blocks.First(x => x.Id == bart.Id) with { AssetId = Guid.NewGuid() };
        again.Prune([.. scene.Blocks.Where(x => x.Id != bart.Id), edited]);
        Assert.Equal(null, again.Of(bart.Id), "cambiar el render descarta la memoria de ese bloque");
        Assert.Equal(FramingMemory.Intended, again.Of(lisa.Id)?.Choice, "los demás siguen igual");
    }

    [Test("Encuadre: personaje diminuto frente a los demás (también un render ancho, con margen) y prop que casi no se ve")]
    public static async Task Sizes()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        var scene = NewScene(folder);
        scene.Show(scene.Bart, Character("izquierda"));
        var tiny = scene.Show(scene.Lisa, Character("derecha") with { VisualMaxWidth = 160, VisualMaxHeight = 180 });
        scene.Pause(1000);
        var sofa = scene.Show(scene.Lisa, Character("derecha") with { VisualMaxWidth = 300, VisualMaxHeight = 200 }, wideRender: true);
        var prop = scene.Prop(new BlockParameters { Position = "centro", VisualMaxWidth = 64, VisualMaxHeight = 64 }); // 64 × 43 px
        scene.Pause(1000);
        var issues = await scene.AuditAsync();

        var small = issues.Single(x => x.BlockId == tiny.Id);
        Assert.Equal(FramingProblem.Small, small.Problem, "Lisa diminuta");
        Assert.True(small.Scale > 2, $"propone agrandarla ({small.Scale})");
        var wide = issues.Single(x => x.BlockId == sofa.Id);
        Assert.Contains("ajuste prudente", wide.Message, "render ancho: margen seguro");
        Assert.Equal(FramingProblem.TinyProp, issues.Single(x => x.BlockId == prop.Id).Problem, "prop diminuto");
        Assert.True(!issues.Any(x => x.Problem == FramingProblem.OutOfFrame), "nada fuera del cuadro");

        // The user's percentage replaces the proposal; the fixes leave no size issue.
        var custom = FramingAudit.ScaleFix(scene.Blocks.First(x => x.Id == tiny.Id), 2);
        Assert.Equal((double?)2, custom.Scale, "el porcentaje del usuario va a la «Escala» del render (1.4.7)");
        var prop2 = FramingAudit.ScaleFix(scene.Blocks.First(x => x.Id == prop.Id), 2);
        Assert.Equal(((int?)128, (int?)128), (prop2.VisualMaxWidth, prop2.VisualMaxHeight), "un prop: su caja × porcentaje");
        foreach (var issue in issues) scene.Fix(issue.BlockId, issue.Fixes[0].Changes);
        var after = await scene.AuditAsync();
        Assert.True(!after.Any(x => x.Problem is FramingProblem.Small or FramingProblem.TinyProp),
            "corregidos: " + string.Join("; ", after.Select(x => x.Message)));
    }
}

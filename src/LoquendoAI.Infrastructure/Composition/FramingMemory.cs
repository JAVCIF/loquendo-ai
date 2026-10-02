using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>
/// One block the framing review looked at: its parameters before any fix (<see cref="OriginalParameters"/>), the
/// problem and its fixes as they were found then, and what the user chose: a fix id («limitar», «entrada», «tamano»),
/// <see cref="FramingMemory.Intended"/> («es a propósito») or <see cref="FramingMemory.Undone"/>. <see cref="Fingerprint"/>
/// is the block after that choice: when the block changes by hand (another render, other numbers), the record no longer
/// applies and the block is reviewed from scratch.
/// </summary>
public sealed record FramingRecord(Guid BlockId, string OriginalParameters, string Fingerprint, string Choice,
    FramingProblem Problem, string Message, IReadOnlyList<FramingRecordFix> Fixes, double? Scale = null);

/// <summary>A fix as stored: its changes as block parameters JSON.</summary>
public sealed record FramingRecordFix(string Id, string Label, string Changes);

/// <summary>
/// The memory of «Revisar encuadre» for one scene (1.4.4), kept in the project: a corrected block can be reviewed
/// again and switched to another fix (from «Limitar al cuadro» to «Era una entrada», or another size) or undone,
/// always starting from the values it had before; a block marked «es a propósito» stops being reported. Pure logic:
/// the window only shows what this returns and writes the blocks it gives back.
/// </summary>
public sealed class FramingMemory
{
    public const string Intended = "a_proposito";
    public const string Undone = "deshecho";

    private readonly Dictionary<Guid, FramingRecord> _records = [];

    public IReadOnlyCollection<FramingRecord> Records => _records.Values;

    public static string Fingerprint(SceneScriptBlock block) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{block.Kind}|{block.AssetId}|{block.ParametersJson}")))[..20];

    public static FramingMemory Load(string path)
    {
        var memory = new FramingMemory();
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<FramingRecord[]>(File.ReadAllText(path)) is { } records)
                foreach (var record in records.Where(x => x is not null)) memory._records[record.BlockId] = record;
        }
        catch (JsonException) { /* A damaged file only loses the memory: the review starts from scratch. */ }
        return memory;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(_records.Values.ToArray(), new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Drops the records of blocks that were deleted or changed by hand since (another render, other values).</summary>
    public void Prune(IReadOnlyList<SceneScriptBlock> blocks)
    {
        var byId = blocks.ToDictionary(x => x.Id);
        foreach (var id in _records.Keys.ToArray())
            if (!byId.TryGetValue(id, out var block) || Fingerprint(block) != _records[id].Fingerprint)
                _records.Remove(id);
    }

    public FramingRecord? Of(Guid blockId) => _records.GetValueOrDefault(blockId);

    /// <summary>Current issues minus the ones marked «es a propósito» (while those blocks stay as they were).</summary>
    public IReadOnlyList<FramingIssue> Pending(IReadOnlyList<FramingIssue> issues) =>
        issues.Where(x => _records.GetValueOrDefault(x.BlockId) is not { Choice: Intended }).ToArray();

    /// <summary>
    /// Applies a choice to a block and remembers it. <paramref name="choice"/>: a fix id of the issue (for a size fix,
    /// <paramref name="scale"/> replaces the proposal), <see cref="Intended"/> (the block stays as it is) or
    /// <see cref="Undone"/> (back to the values it had before the first fix). The issue comes from the current review;
    /// for a block corrected before, the remembered issue and original values are used, so switching fixes never
    /// stacks one fix on another. Returns the block to save.
    /// </summary>
    public SceneScriptBlock Choose(SceneScriptBlock block, FramingIssue? issue, string choice, double? scale = null)
    {
        var record = _records.GetValueOrDefault(block.Id);
        if (record is null)
        {
            if (issue is null) return block;
            record = new FramingRecord(block.Id, block.ParametersJson, "", "", issue.Problem, issue.Message,
                issue.Fixes.Select(x => new FramingRecordFix(x.Id, x.Label, x.Changes.ToJson())).ToArray(), issue.Scale);
        }
        else if (issue?.Fixes.FirstOrDefault(x => x.Id == choice) is { } offered && record.Fixes.All(x => x.Id != choice))
            // A fix the remembered review did not have (the strict review offers «Cambiar tamaño» for any render).
            record = record with { Fixes = [.. record.Fixes, new FramingRecordFix(offered.Id, offered.Label, offered.Changes.ToJson())] };
        var original = BlockParameters.Parse(record.OriginalParameters);
        SceneScriptBlock result;
        if (choice == Intended || choice == Undone)
            result = block with { ParametersJson = choice == Undone ? record.OriginalParameters : block.ParametersJson };
        else
        {
            var fix = record.Fixes.FirstOrDefault(x => x.Id == choice)
                ?? throw new InvalidOperationException($"No hay una corrección «{choice}» para este bloque.");
            var changes = fix.Id == "tamano" && scale is double custom
                ? FramingAudit.ScaleFix(block with { ParametersJson = record.OriginalParameters }, custom)
                : BlockParameters.Parse(fix.Changes);
            // Size and place are independent (1.4.7): a size fix keeps the place the block has now (a limited move) and
            // a place fix keeps its size; each one starts from the original values of its own fields.
            var now = BlockParameters.Of(block);
            var basis = fix.Id == "tamano"
                ? original with
                {
                    VisualOffsetX = now.VisualOffsetX, VisualOffsetY = now.VisualOffsetY,
                    MotionOffsetX = now.MotionOffsetX, MotionOffsetY = now.MotionOffsetY
                }
                : original with
                {
                    Scale = now.Scale, VisualMaxWidth = now.VisualMaxWidth, VisualMaxHeight = now.VisualMaxHeight,
                    FramingPreset = now.FramingPreset
                };
            result = block with { ParametersJson = FramingAudit.Apply(basis, changes).ToJson() };
        }
        _records[block.Id] = record with { Fingerprint = Fingerprint(result), Choice = choice, Scale = scale ?? record.Scale };
        return result;
    }
}

using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Persistence;

/// <summary>Catalog search used by the resource picker: filters by file extension (what the
/// block can play), optionally by catalogue category, source and character, and matches every
/// search word against name, relative path and AI/manual tags.</summary>
public sealed record AssetPickerQuery(
    IReadOnlyCollection<string> Extensions,
    IReadOnlyCollection<AssetKind>? Kinds,
    string Search,
    Guid? SourceId = null,
    string? CharacterName = null,
    int Limit = 400);

/// <summary>An asset plus the tags a human needs to recognise it. Manual tags win over AI ones.</summary>
public sealed record AssetPickerItem(
    AssetRecord Asset,
    string Description,
    string Mood,
    string Role,
    string Subject);

/// <summary>Builds FTS5 queries for the asset search index (schema v6).</summary>
public static class AssetSearchText
{
    /// <summary>CTE "hits(hit_id, score)" for $match; lower score = better (bm25).</summary>
    public const string RankedHits = """
WITH hits(hit_id, score) AS (
    SELECT m.asset_id, bm25(asset_search, 8.0, 3.0, 2.0)
    FROM asset_search JOIN asset_fts_map m ON m.doc = asset_search.rowid
    WHERE asset_search MATCH $match)

""";

    /// <summary>Every word must appear as a word prefix: «bart eno» → "bart"* AND "eno"*.
    /// Quotes keep FTS operators (OR, NOT, -, :) typed by the user as plain text.</summary>
    public static string? FtsMatch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var words = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Replace("\"", ""))
            .Where(word => word.Any(char.IsLetterOrDigit))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8)
            .Select(word => "\"" + word + "\"*").ToArray();
        return words.Length == 0 ? null : string.Join(" ", words);
    }
}

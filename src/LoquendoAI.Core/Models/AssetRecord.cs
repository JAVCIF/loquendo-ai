namespace LoquendoAI.Core.Models;

public sealed record AssetRecord(
    Guid Id,
    string RelativePath,
    AssetKind Kind,
    string Sha256,
    string DisplayName,
    DateTimeOffset ImportedUtc,
    Guid? SourceId = null,
    string? SourceRelativePath = null,
    long FileSize = 0,
    DateTimeOffset? LastWriteUtc = null,
    string Extension = "",
    CutoutStatus CutoutStatus = CutoutStatus.Unknown,
    string? SubjectName = null,
    string? CollectionName = null,
    bool IsMissing = false);

public sealed record AssetTag(Guid AssetId, string Key, string Value, double Confidence = 1.0);

public sealed record AssetSource(
    Guid Id,
    string Name,
    string RootPath,
    bool Enabled,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastScanUtc = null);

public sealed record FolderRule(
    Guid Id,
    Guid SourceId,
    string RelativeFolder,
    FolderClassification Classification,
    string? SubjectName,
    string? CollectionName,
    bool IncludeSubfolders = true,
    CutoutStatus DefaultCutoutStatus = CutoutStatus.Unknown);

public sealed record AssetImportSummary(
    int FilesSeen,
    int Imported,
    int Updated,
    int Skipped,
    int Missing,
    int NeedsCutout,
    int Errors,
    IReadOnlyList<string> ErrorMessages,
    int Moved = 0);

public sealed record AssetFolder(Guid SourceId, string RelativePath);

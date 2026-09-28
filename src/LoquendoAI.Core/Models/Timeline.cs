namespace LoquendoAI.Core.Models;

public sealed record Episode(
    Guid Id,
    int Number,
    string Title,
    string? Synopsis = null);

public sealed record Scene(
    Guid Id,
    Guid EpisodeId,
    int Index,
    string Title,
    long DurationMs,
    Guid? BackgroundAssetId = null,
    string? DirectionNotes = null);

namespace LoquendoAI.TtsBridge32;

public sealed record ProviderInfo(
    string Key,
    string Name,
    bool Available,
    string? Detail = null,
    string? Path = null,
    string? Version = null,
    IReadOnlyList<string>? Voices = null);

public sealed record SynthRequest(
    string Provider,
    string Voice,
    string Text,
    string Output,
    int? Rate = null,
    int? Volume = null,
    int SampleRate = 32000,
    int Channels = 1,
    string? EnginePath = null,
    int? Pitch = null);

public sealed record BridgeRequest(
    string Command,
    string? Provider = null,
    string? Voice = null,
    string? Text = null,
    string? Output = null,
    int? Rate = null,
    int? Volume = null,
    int SampleRate = 32000,
    int Channels = 1,
    string? EnginePath = null,
    int? Pitch = null);

public sealed record BridgeResponse(
    bool Ok,
    string? Error = null,
    object? Data = null);

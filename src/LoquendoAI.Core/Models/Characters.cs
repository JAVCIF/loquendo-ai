namespace LoquendoAI.Core.Models;

public sealed record CharacterDefinition(
    Guid Id,
    string Name,
    Guid? DefaultVoiceProfileId,
    string? Notes = null);

/// <summary>
/// Persistent base voice identity for a character. Prosody values are intentionally
/// provider-specific: Loquendo TTS7 pitch/speed use the native 0..100 scale, while
/// SAPI5 uses its own ranges. A null speed means "use the engine/voice default".
/// </summary>
public sealed record VoiceProfile(
    Guid Id,
    string Name,
    string ProviderKey,
    string VoiceId,
    int? Pitch = null,
    int? Speed = null,
    int Volume = 100,
    int SampleRate = 32000,
    string ConfigJson = "{}");

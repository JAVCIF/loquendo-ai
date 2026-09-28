namespace LoquendoAI.Core.Models;

public enum ScriptBlockKind
{
    Dialogue = 0,
    Narration,
    Background,
    CharacterShow,
    CharacterHide,
    Image,
    SoundEffect,
    Music,
    Video,
    Pause,
    TextOverlay,
    Transition,
    Comment,
    /// <summary>Camera move (1.0.0-beta.5): frames a character, whoever speaks, a point or the whole
    /// frame. Appended last so the stored numbers of the other kinds do not change.</summary>
    Camera,
    /// <summary>Cinematic black bars (1.0.0-beta.5): shows or removes the letterbox.</summary>
    Cinema,
    /// <summary>Character gesture (1.0.0-beta.7): a tilt («balanceo») and/or a stretch bounce («rebote») of
    /// the render around its feet, as VEGAS Pan/Crop keyframes.</summary>
    Gesture,
    /// <summary>Blur (1.0.0-beta.8): VEGAS «Desenfoque gaussiano» on the background, a character or other layers.</summary>
    Blur
}

/// <summary>
/// A single ordered instruction in a scene script. Dialogue/narration blocks can resolve a
/// character voice profile and produce a cached WAV. Visual/audio action blocks may point to
/// an asset while ParametersJson keeps room for future editor-specific details.
/// </summary>
public sealed record SceneScriptBlock(
    Guid Id,
    Guid SceneId,
    int OrderIndex,
    ScriptBlockKind Kind,
    Guid? CharacterId = null,
    string Text = "",
    Guid? AssetId = null,
    Guid? VoiceProfileId = null,
    int? VoicePitchOverride = null,
    int? VoiceSpeedOverride = null,
    int? VoiceVolumeOverride = null,
    int PauseAfterMs = 0,
    string ParametersJson = "{}",
    long? StartOffsetMs = null,
    string? GeneratedAudioPath = null,
    string? GeneratedAudioHash = null,
    long? GeneratedDurationMs = null);

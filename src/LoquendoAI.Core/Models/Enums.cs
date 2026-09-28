namespace LoquendoAI.Core.Models;

public enum AssetKind
{
    Unknown = 0,
    CharacterSprite,
    Background,
    Prop,
    SoundEffect,
    Music,
    Video,
    Meme,
    Overlay,
    Font,
    VisualEffect,
    Audio
}

public enum FolderClassification
{
    Auto = 0,
    CharacterRenders,
    Backgrounds,
    SoundEffects,
    Music,
    Videos,
    VisualEffects,
    Memes,
    Props,
    Mixed,
    Ignore
}

public enum CutoutStatus
{
    Unknown = 0,
    Ready,
    NeedsCutout,
    IntentionallyOpaque,
    NotApplicable
}

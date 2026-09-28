using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using LoquendoAI.Infrastructure.Dialogue;
using LoquendoAI.Infrastructure.Tts;

namespace LoquendoAI.App;

/// <summary>
/// A row of the dialogue module (1.2.0). The window keeps the derived parts up to date (who the speaker is, the
/// voice, the audio state) through <see cref="MainWindow"/>.RefreshDialogueRow whenever an editable part changes.
/// </summary>
public sealed class DialogueRow : INotifyPropertyChanged
{
    public static readonly string[] KindNames = ["Diálogo", "Narración", "▬ Escena"];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }

    private int _order;
    public int Order { get => _order; set => Set(ref _order, value); }

    private DialogueKind _kind;
    public DialogueKind Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            Notify(nameof(KindText));
            Notify(nameof(IsLine));
            Notify(nameof(LineVisibility));
        }
    }

    public string KindText
    {
        get => KindNames[(int)Kind];
        set
        {
            var index = Array.IndexOf(KindNames, value);
            if (index >= 0) Kind = (DialogueKind)index;
        }
    }

    public IReadOnlyList<string> KindChoices => KindNames;
    public bool IsLine => Kind != DialogueKind.SceneBreak;
    public Visibility LineVisibility => IsLine ? Visibility.Visible : Visibility.Collapsed;

    private string _speaker = "";
    /// <summary>At most 40 characters, without «:» or «|» (they would break the text format and Director lines).</summary>
    public string Speaker
    {
        get => _speaker;
        set
        {
            var clean = (value ?? "").Replace(':', ' ').Replace('|', ' ').Trim();
            if (!Set(ref _speaker, clean.Length > 40 ? clean[..40].Trim() : clean) && clean != (value ?? "").Trim()) Notify();
        }
    }

    /// <summary>The registered character of <see cref="Speaker"/> (null for the narrator and unregistered NPCs).</summary>
    public Guid? CharacterId { get; set; }

    /// <summary>NPC rows: the profile chosen for this line (the NPC profile by default, or any saved profile) and,
    /// with the NPC profile, its own voice (any engine of the selector since 1.4.0; no provider = Loquendo TTS7).
    /// Rows of a character with its own profile use that profile.</summary>
    public Guid? ProfileId { get; set; }
    public string? DirectVoice { get; set; }
    public string? DirectProvider { get; set; }

    public DirectVoiceRef? Direct => string.IsNullOrWhiteSpace(DirectVoice) ? null
        : new DirectVoiceRef(VoiceSelectorSettings.Canonical(DirectProvider ?? VoiceSelectorSettings.Tts7), DirectVoice);

    private static bool SameVoice(DirectVoiceRef? a, DirectVoiceRef? b) =>
        a is null ? b is null : b is not null && a.Is(b.Provider, b.Voice);

    private bool _isNpc;
    public bool IsNpc
    {
        get => _isNpc;
        set
        {
            if (!Set(ref _isNpc, value)) return;
            Notify(nameof(NpcVisibility));
            Notify(nameof(ProfileVisibility));
        }
    }

    public Visibility NpcVisibility => IsLine && IsNpc ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProfileVisibility => IsLine && !IsNpc ? Visibility.Visible : Visibility.Collapsed;

    public void NotifyLayout()
    {
        Notify(nameof(NpcVisibility));
        Notify(nameof(ProfileVisibility));
        Notify(nameof(LineVisibility));
    }

    private string _voiceText = "";
    public string VoiceText { get => _voiceText; set => Set(ref _voiceText, value); }

    private IReadOnlyList<VoiceProfileChoice> _voiceChoices = [];
    public IReadOnlyList<VoiceProfileChoice> VoiceChoices
    {
        get => _voiceChoices;
        set
        {
            if (ReferenceEquals(_voiceChoices, value)) return;
            _voiceChoices = value;
            Notify();
            Notify(nameof(VoiceChoice));
        }
    }

    private IReadOnlyList<string> _speakerChoices = [];
    public IReadOnlyList<string> SpeakerChoices { get => _speakerChoices; set => Set(ref _speakerChoices, value); }

    /// <summary>The NPC voice shown in the cell; choosing one sets <see cref="ProfileId"/> and <see cref="DirectVoice"/>.</summary>
    public VoiceProfileChoice? VoiceChoice
    {
        get => VoiceChoices.FirstOrDefault(c => c.Id == (ProfileId ?? MainWindow.NpcProfileId) && SameVoice(c.Direct, Direct))
               ?? VoiceChoices.FirstOrDefault(c => c.Id == (ProfileId ?? MainWindow.NpcProfileId) && c.Direct is null);
        set
        {
            if (value is null) return;
            if (ProfileId == value.Id && SameVoice(Direct, value.Direct)) return;
            ProfileId = value.Id;
            DirectVoice = value.Direct?.Voice;
            DirectProvider = value.Direct?.Provider;
            Notify();
        }
    }

    public int? Pitch { get; set; }
    public int? Speed { get; set; }
    public int? Volume { get; set; }

    /// <summary>Per-line prosody (session tweak, the profile is not changed): empty = the profile's value.</summary>
    // Loquendo TTS7 and SAPI4 use 0–100, SAPI5 −10…10 (as in Voice Lab).
    public string PitchText { get => NumberText(Pitch); set => SetNumber(value, x => Pitch = x, -10, 100); }
    public string SpeedText { get => NumberText(Speed); set => SetNumber(value, x => Speed = x, -10, 100); }
    public string VolumeText { get => NumberText(Volume); set => SetNumber(value, x => Volume = x, 0, 100); }

    private static string NumberText(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private void SetNumber(string? text, Action<int?> assign, int min, int max, [CallerMemberName] string? name = null)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) assign(null);
        else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) assign(Math.Clamp(value, min, max));
        Notify(name); // an invalid number goes back to the previous value
    }

    public void NotifyVoiceChoice() => Notify(nameof(VoiceChoice));

    public void NotifyProsody()
    {
        Notify(nameof(PitchText));
        Notify(nameof(SpeedText));
        Notify(nameof(VolumeText));
    }

    private string _text = "";
    public string Text { get => _text; set => Set(ref _text, (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim()); }

    private string _comment = "";
    public string Comment { get => _comment; set => Set(ref _comment, (value ?? "").Trim()); }

    /// <summary>The voice-cache hash of the audio this line has (it is current while it equals the hash of its
    /// voice, prosody and text).</summary>
    public string? AudioHash { get; set; }

    private long? _durationMs;
    public long? DurationMs
    {
        get => _durationMs;
        set { if (Set(ref _durationMs, value)) Notify(nameof(DurationText)); }
    }

    public string DurationText => DurationMs is long ms ? $"{ms / 1000d:0.00} s" : "";

    private string _stateText = "";
    public string StateText { get => _stateText; set => Set(ref _stateText, value); }

    private string _stateDetail = "";
    public string StateDetail { get => _stateDetail; set => Set(ref _stateDetail, value); }

    /// <summary>What the importer had to guess (kept until the row is edited).</summary>
    public string? ParseWarning { get; set; }

    private bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value); }

    public DialogueEntry ToEntry() => new(Kind, Kind == DialogueKind.Narration ? DialogueScript.Narrator : Speaker, Text, Comment);

    public DialogueRow Clone() => new()
    {
        Kind = Kind, Speaker = Speaker, ProfileId = ProfileId, DirectVoice = DirectVoice, DirectProvider = DirectProvider,
        Pitch = Pitch, Speed = Speed,
        Volume = Volume, Text = Text, Comment = Comment, AudioHash = AudioHash, DurationMs = DurationMs
    };
}

/// <summary>A dialogue script saved in the project folder «dialogos».</summary>
public sealed record DialogueSetChoice(string Name, string Path);

/// <summary>Target of «Exportar a escenas»: an episode of the project, or a new one (null id).</summary>
public sealed record DialogueEpisodeChoice(Guid? Id, string Name);

/// <summary>JSON file of a dialogue script (dialogos/*.json).</summary>
internal sealed class DialogueDocument
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public List<DialogueLineData> Lines { get; set; } = [];
    public DialogueAiData Ai { get; set; } = new();
}

internal sealed class DialogueLineData
{
    public string Kind { get; set; } = "dialogo";
    public string Speaker { get; set; } = "";
    public string Text { get; set; } = "";
    public string Comment { get; set; } = "";
    public Guid? Profile { get; set; }
    public string? Voice { get; set; }
    /// <summary>Engine of <see cref="Voice"/> (1.4.0); absent = Loquendo TTS7, as files of 1.2–1.3.0 were.</summary>
    public string? VoiceProvider { get; set; }
    public int? Pitch { get; set; }
    public int? Speed { get; set; }
    public int? Volume { get; set; }
    public string? Hash { get; set; }
    public long? DurationMs { get; set; }
    public string? Warning { get; set; }
}

/// <summary>What «Prompt maestro…» remembers for this script: characters, extra names, story and length.</summary>
internal sealed class DialogueAiData
{
    public List<Guid> Characters { get; set; } = [];
    public string Extras { get; set; } = "";
    public string Story { get; set; } = "";
    public int Lines { get; set; } = 30;
}

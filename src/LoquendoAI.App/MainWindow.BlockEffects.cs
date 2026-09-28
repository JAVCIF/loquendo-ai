using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.App;

/// <summary>
/// Editor panels of the Camera, Cinema, Gesture and Blur blocks (1.1.1). They write the same parameters as the
/// Director lines [CAMARA], [CINE], [GESTO] and [DESENFOQUE], and a block loaded here shows what those lines set,
/// validated the same way as the preview and the VEGAS export read them (BlockParameters.Camera/Cinema/…).
/// </summary>
public partial class MainWindow
{
    private static readonly string[] CameraModes = ["personaje", "habla", "punto", "general"];
    private static readonly string[] GestureModes = ["personaje", "habla", "quitar"];
    private static readonly string[] GestureSides = ["auto", "derecha", "izquierda"];
    private static readonly string[] GesturePivots = ["pies", "cintura", "centro"];
    private static readonly string[] BlurTargets = ["personaje", "fondo", "personajes", "imagenes", "videos", "todos"];
    private static readonly double[] BlurStyleAmounts = [BlurPlan.Soft, BlurPlan.Light, 0];
    private bool _syncingBlurStyle;

    private static bool IsEffectBlock(ScriptBlockKind kind) =>
        kind is ScriptBlockKind.Camera or ScriptBlockKind.Cinema or ScriptBlockKind.Gesture or ScriptBlockKind.Blur;

    /// <summary>Whether the block uses the «Personaje» box with the options chosen now.</summary>
    private bool EffectUsesCharacter(ScriptBlockKind kind) => kind switch
    {
        ScriptBlockKind.Camera => CameraModeCombo.SelectedIndex == 0,
        ScriptBlockKind.Gesture => GestureModeCombo.SelectedIndex == 0,
        ScriptBlockKind.Blur => BlurTargetCombo.SelectedIndex == 0,
        _ => false
    };

    private void ResetEffectControls()
    {
        CameraModeCombo.SelectedIndex = 0;
        CameraZoomBox.Text = Number(BlockDefaults.CameraZoom);
        CameraMoveBox.Text = BlockDefaults.CameraMoveMs.ToString(CultureInfo.InvariantCulture);
        CameraFocusCombo.SelectedIndex = 0;
        CameraOffsetXBox.Text = CameraOffsetYBox.Text = "0";

        CinemaModeCombo.SelectedIndex = 0;
        CinemaStyleCombo.SelectedIndex = 0;
        CinemaMoveBox.Text = BlockDefaults.CinemaMoveMs.ToString(CultureInfo.InvariantCulture);
        CinemaLayersCombo.SelectedIndex = 0;
        CinemaCharactersList.ItemsSource = CinemaCharacterChoices();

        GestureModeCombo.SelectedIndex = 0;
        GestureStepsCombo.Text = "balanceo";
        GestureAngleBox.Text = Number(BlockDefaults.GestureAngle);
        GestureStretchBox.Text = Number(BlockDefaults.GestureStretch);
        GestureSideCombo.SelectedIndex = 0;
        GestureSpeedBox.Text = "1";
        GesturePivotCombo.SelectedIndex = 0;

        BlurTargetCombo.SelectedIndex = 0;
        SetBlurAmount(BlurPlan.Soft);
        BlurMoveBox.Text = "0";
    }

    private void LoadEffectControls(SceneScriptBlock block)
    {
        ResetEffectControls();
        var parameters = BlockParameters.Of(block);
        var hasCharacter = block.CharacterId is not null;
        switch (block.Kind)
        {
            case ScriptBlockKind.Camera:
            {
                var camera = parameters.Camera(hasCharacter);
                CameraModeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(CameraModes, camera.Mode));
                // The general shot is always 1×: show the zoom it had before (or the default) for a later change of mode.
                CameraZoomBox.Text = Number(camera.Mode == "general" ? parameters.CameraZoom is { } z && z >= BlockDefaults.CameraZoomMin &&
                    z <= BlockDefaults.CameraZoomMax ? z : BlockDefaults.CameraZoom : camera.Zoom);
                CameraMoveBox.Text = camera.MoveMs.ToString(CultureInfo.InvariantCulture);
                CameraFocusCombo.SelectedIndex = camera.Focus == "cuerpo" ? 1 : 0;
                CameraOffsetXBox.Text = camera.OffsetX.ToString(CultureInfo.InvariantCulture);
                CameraOffsetYBox.Text = camera.OffsetY.ToString(CultureInfo.InvariantCulture);
                break;
            }
            case ScriptBlockKind.Cinema:
            {
                var cinema = parameters.Cinema();
                CinemaModeCombo.SelectedIndex = cinema.Show ? 0 : 1;
                CinemaStyleCombo.SelectedIndex = cinema.Style == "abierto" ? 1 : 0;
                CinemaMoveBox.Text = cinema.MoveMs.ToString(CultureInfo.InvariantCulture);
                CinemaLayersCombo.SelectedIndex = cinema.Layers switch { "personajes" => 1, "lista" => 2, _ => 0 };
                CinemaCharactersList.SelectedItems.Clear();
                foreach (var choice in CinemaCharactersList.Items.OfType<CharacterChoice>())
                    if (choice.Id is Guid id && cinema.Characters.Contains(id)) CinemaCharactersList.SelectedItems.Add(choice);
                break;
            }
            case ScriptBlockKind.Gesture:
            {
                var gesture = parameters.Gesture();
                GestureModeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(GestureModes, gesture.Mode));
                GestureStepsCombo.Text = GestureSettings.StepsText(gesture.Steps);
                GestureAngleBox.Text = Number(gesture.Angle);
                GestureStretchBox.Text = Number(gesture.StretchPercent);
                GestureSideCombo.SelectedIndex = Math.Max(0, Array.IndexOf(GestureSides, gesture.Side));
                GestureSpeedBox.Text = Number(gesture.Speed);
                GesturePivotCombo.SelectedIndex = Math.Max(0, Array.IndexOf(GesturePivots, gesture.Pivot));
                break;
            }
            case ScriptBlockKind.Blur:
            {
                var blur = parameters.Blur(hasCharacter);
                BlurTargetCombo.SelectedIndex = Math.Max(0, Array.IndexOf(BlurTargets, blur.Target));
                SetBlurAmount(blur.Amount);
                BlurMoveBox.Text = blur.MoveMs.ToString(CultureInfo.InvariantCulture);
                break;
            }
        }
    }

    private void UpdateEffectPanels(ScriptBlockKind kind)
    {
        CameraOptionsPanel.Visibility = kind == ScriptBlockKind.Camera ? Visibility.Visible : Visibility.Collapsed;
        CinemaOptionsPanel.Visibility = kind == ScriptBlockKind.Cinema ? Visibility.Visible : Visibility.Collapsed;
        GestureOptionsPanel.Visibility = kind == ScriptBlockKind.Gesture ? Visibility.Visible : Visibility.Collapsed;
        BlurOptionsPanel.Visibility = kind == ScriptBlockKind.Blur ? Visibility.Visible : Visibility.Collapsed;

        var cameraMode = CameraModeCombo.SelectedIndex;
        CameraZoomBox.IsEnabled = cameraMode != 3;
        CameraFocusCombo.IsEnabled = cameraMode is 0 or 1;
        CameraOffsetXBox.IsEnabled = CameraOffsetYBox.IsEnabled = cameraMode != 3;

        var showBars = CinemaModeCombo.SelectedIndex == 0;
        CinemaStyleCombo.IsEnabled = CinemaLayersCombo.IsEnabled = showBars;
        CinemaCharactersList.Visibility = showBars && CinemaLayersCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

        var gestures = GestureModeCombo.SelectedIndex != 2;
        GestureStepsCombo.IsEnabled = GestureAngleBox.IsEnabled = GestureStretchBox.IsEnabled =
            GestureSideCombo.IsEnabled = GestureSpeedBox.IsEnabled = GesturePivotCombo.IsEnabled = gestures;
    }

    /// <summary>The parameters of an effect block from its panel, or the message that explains what is wrong.
    /// Keys the editor does not show (and any unknown key) are kept from the saved block.</summary>
    private bool TryEffectParameters(ScriptBlockKind kind, SceneScriptBlock? previous, Guid? characterId,
        out string json, out string? error)
    {
        json = "{}";
        error = null;
        var saved = previous?.Kind == kind ? BlockParameters.Of(previous) : BlockParameters.Empty;
        switch (kind)
        {
            case ScriptBlockKind.Camera:
            {
                var mode = CameraModes[Math.Clamp(CameraModeCombo.SelectedIndex, 0, CameraModes.Length - 1)];
                if (mode == "personaje" && characterId is null)
                    return Fail("Cámara: elige el personaje a encuadrar o cambia a «Seguir a quien habla», «Punto» o «Plano general».", out error);
                // The general shot ignores zoom and X/Y (greyed out): a leftover invalid value there does not block saving.
                double zoom = 1;
                long x = 0, y = 0;
                var general = mode == "general";
                if (!TryWhole(CameraMoveBox, 0, BlockDefaults.CameraMoveMaxMs, out var move) || !general &&
                    (!TryNumber(CameraZoomBox, BlockDefaults.CameraZoomMin, BlockDefaults.CameraZoomMax, out zoom) ||
                     !TryWhole(CameraOffsetXBox, -640, 640, out x) || !TryWhole(CameraOffsetYBox, -360, 360, out y)))
                    return Fail("Cámara: zoom de 1 a 3, movimiento de 0 a 5000 ms, ajuste X ±640 e Y ±360.", out error);
                json = (saved with
                {
                    CameraMode = mode,
                    CameraZoom = general ? null : zoom,
                    CameraMoveMs = (long)move,
                    CameraFocus = CameraFocusCombo.SelectedIndex == 1 ? "cuerpo" : "cara",
                    CameraOffsetX = general ? null : (int)x,
                    CameraOffsetY = general ? null : (int)y
                }).ToJson();
                return true;
            }
            case ScriptBlockKind.Cinema:
            {
                var show = CinemaModeCombo.SelectedIndex == 0;
                if (!TryWhole(CinemaMoveBox, 0, BlockDefaults.CameraMoveMaxMs, out var move))
                    return Fail("Cine: la transición de las barras va de 0 a 5000 ms.", out error);
                var layers = CinemaLayersCombo.SelectedIndex switch { 1 => "personajes", 2 => "lista", _ => "todos" };
                var chosen = CinemaCharactersList.SelectedItems.OfType<CharacterChoice>().Select(x => x.Id).OfType<Guid>().Distinct().ToArray();
                if (show && layers == "lista" && chosen.Length == 0)
                    return Fail("Cine: marca al menos un personaje en la lista o elige «Todas las capas».", out error);
                json = (saved with
                {
                    CinemaMode = show ? "mostrar" : "quitar",
                    CinemaStyle = CinemaStyleCombo.SelectedIndex == 1 ? "abierto" : "cerrado",
                    CinemaMoveMs = (long)move,
                    CinemaLayers = layers,
                    CinemaCharacters = layers == "lista" ? chosen : null
                }).ToJson();
                return true;
            }
            case ScriptBlockKind.Gesture:
            {
                var mode = GestureModes[Math.Clamp(GestureModeCombo.SelectedIndex, 0, GestureModes.Length - 1)];
                if (mode == "personaje" && characterId is null)
                    return Fail("Gesto: elige el personaje o cambia a «Quien habla, desde aquí».", out error);
                var stepsText = GestureStepsCombo.Text.Trim();
                var steps = GestureSettings.ParseSteps(stepsText);
                if (mode != "quitar" && (steps.Count == 0 || stepsText.Split([',', '>', ';', '+', '&', '/'],
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(w => GestureSettings.Move(w) is null)))
                    return Fail("Gesto: los movimientos son «balanceo», «rebote» o «ambos», unidos con «+» (a la vez) o «,» (uno tras otro).", out error);
                if (mode == "quitar")
                {
                    // «Dejar de gesticular» only ends «Quien habla»: the other (greyed-out) fields are not read.
                    json = (saved with { GestureMode = mode }).ToJson();
                    return true;
                }
                if (!TryNumber(GestureAngleBox, 0.5, BlockDefaults.GestureAngleMax, out var angle) ||
                    !TryNumber(GestureStretchBox, BlockDefaults.GestureStretchMin, BlockDefaults.GestureStretchMax, out var stretch) || Math.Abs(stretch) < 1 ||
                    !TryNumber(GestureSpeedBox, BlockDefaults.GestureSpeedMin, BlockDefaults.GestureSpeedMax, out var speed))
                    return Fail("Gesto: ángulo de 0.5 a 20°, estirar de -30 a 40 % (al menos ±1) y velocidad de 0.5 a 3.", out error);
                json = (saved with
                {
                    GestureMode = mode,
                    GestureSteps = GestureSettings.StepsText(steps),
                    GestureAngle = angle,
                    GestureSide = GestureSides[Math.Clamp(GestureSideCombo.SelectedIndex, 0, GestureSides.Length - 1)],
                    GestureStretch = stretch,
                    GestureSpeed = speed,
                    GesturePivot = GesturePivots[Math.Clamp(GesturePivotCombo.SelectedIndex, 0, GesturePivots.Length - 1)]
                }).ToJson();
                return true;
            }
            case ScriptBlockKind.Blur:
            {
                var target = BlurTargets[Math.Clamp(BlurTargetCombo.SelectedIndex, 0, BlurTargets.Length - 1)];
                if (target == "personaje" && characterId is null)
                    return Fail("Desenfoque: elige el personaje o cambia el objetivo (por ejemplo, «Fondo»).", out error);
                if (!TryNumber(BlurAmountBox, 0, BlurPlan.Max, out var amount) ||
                    !TryWhole(BlurMoveBox, 0, BlockDefaults.CameraMoveMaxMs, out var move))
                    return Fail("Desenfoque: valor de 0 a 0.1 (0.02 suavizar, 0.01 ligero, 0 nítido) y transición de 0 a 5000 ms.", out error);
                json = (saved with { BlurTarget = target, BlurAmount = amount, BlurMoveMs = (long)move }).ToJson();
                return true;
            }
        }
        return true;
    }

    private void EffectOption_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_scriptUiReady || _loadingScriptUi) return;
        // «Punto del cuadro» starts at 1.3× like «[CAMARA] punto»; the other modes at 1.4×. Only an untouched
        // default is swapped, never a zoom the user typed.
        if (ReferenceEquals(sender, CameraModeCombo) && ParseNumber(CameraZoomBox.Text) is double current)
        {
            var point = CameraModeCombo.SelectedIndex == 2;
            if (point && current == BlockDefaults.CameraZoom) CameraZoomBox.Text = Number(BlockDefaults.CameraPointZoom);
            else if (!point && current == BlockDefaults.CameraPointZoom) CameraZoomBox.Text = Number(BlockDefaults.CameraZoom);
        }
        UpdateScriptBlockEditorState();
    }

    /// <summary>Characters added or renamed while a Cine block is open (keeps the ticks).</summary>
    private void RefreshCinemaCharacterChoices()
    {
        if (CinemaCharactersList is null) return;
        var ticked = CinemaCharactersList.SelectedItems.OfType<CharacterChoice>().Select(x => x.Id).ToHashSet();
        var choices = CinemaCharacterChoices();
        CinemaCharactersList.ItemsSource = choices;
        foreach (var choice in choices)
            if (ticked.Contains(choice.Id)) CinemaCharactersList.SelectedItems.Add(choice);
    }

    private void BlurStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_scriptUiReady || _syncingBlurStyle) return;
        var index = BlurStyleCombo.SelectedIndex;
        if (index >= 0 && index < BlurStyleAmounts.Length) SetBlurAmount(BlurStyleAmounts[index]);
    }

    private void BlurAmountBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_scriptUiReady || _syncingBlurStyle) return;
        _syncingBlurStyle = true;
        try { BlurStyleCombo.SelectedIndex = BlurStyleIndex(ParseNumber(BlurAmountBox.Text)); }
        finally { _syncingBlurStyle = false; }
    }

    private void SetBlurAmount(double amount)
    {
        _syncingBlurStyle = true;
        try
        {
            BlurAmountBox.Text = amount.ToString("0.######", CultureInfo.InvariantCulture);
            BlurStyleCombo.SelectedIndex = BlurStyleIndex(amount);
        }
        finally { _syncingBlurStyle = false; }
    }

    private static int BlurStyleIndex(double? amount)
    {
        if (amount is not double value) return 3;
        var index = Array.FindIndex(BlurStyleAmounts, x => Math.Abs(x - value) < 0.00005);
        return index >= 0 ? index : 3;
    }

    private CharacterChoice[] CinemaCharacterChoices() =>
        _characters.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => new CharacterChoice(x.Id, x.Name)).ToArray();

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }

    /// <summary>Accepts «1.4» and «1,4» (Spanish decimal comma).</summary>
    private static double? ParseNumber(string text) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) ? value : null;

    private static bool TryNumber(TextBox box, double min, double max, out double value)
    {
        value = ParseNumber(box.Text) ?? double.NaN;
        return value >= min && value <= max;
    }

    private static bool TryWhole(TextBox box, long min, long max, out long value) =>
        long.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    private static string Number(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
}

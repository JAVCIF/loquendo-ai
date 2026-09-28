using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.App;

/// <summary>
/// Moving scenes in the Editor (1.4.0):
/// <list type="bullet">
/// <item>«Zoom» of a background: a background larger than the frame (up to 300 %, 3840×2160) can be offset or pan
/// without showing black edges, and keeps the same scale when a later block of the same background starts to move.</item>
/// <item>«↳ Seguir desde el anterior»: a block starts where the previous block of the same background, character,
/// image or video ended (size/zoom, final position and rotation), so a walk or a pan continues without a jump.</item>
/// </list>
/// </summary>
public partial class MainWindow
{
    private bool _settingBackgroundZoom;

    private void BackgroundZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingScriptUi || _settingBackgroundZoom || BackgroundZoomCombo.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom)) return;
        VisualWidthBox.Text = ((int)Math.Round(1280 * zoom)).ToString(CultureInfo.InvariantCulture);
        VisualHeightBox.Text = ((int)Math.Round(720 * zoom)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Ancho/Alto typed by hand: the zoom shown follows them.</summary>
    private void VisualSizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Also raised while the window is being built (XAML Text=…), before every control exists.
        if (BackgroundZoomCombo is null || VisualWidthBox is null || VisualHeightBox is null || _settingBackgroundZoom) return;
        SyncBackgroundZoomCombo();
    }

    /// <summary>Shows the zoom that matches Ancho/Alto (none for a size of its own).</summary>
    private void SyncBackgroundZoomCombo()
    {
        _settingBackgroundZoom = true;
        try
        {
            int.TryParse(VisualWidthBox.Text.Trim(), out var width);
            int.TryParse(VisualHeightBox.Text.Trim(), out var height);
            BackgroundZoomCombo.SelectedItem = BackgroundZoomCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                item.Tag is string tag && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) &&
                (int)Math.Round(1280 * zoom) == width && (int)Math.Round(720 * zoom) == height);
        }
        finally { _settingBackgroundZoom = false; }
    }

    /// <summary>
    /// Advice after saving a background. Still and moved to one side with too little zoom: black edges show. Moving
    /// with too little zoom: it still covers the frame (it grows just enough) but changes scale when it starts to move.
    /// Says which zoom avoids it. Backgrounds smaller than the frame (a box on purpose) are left alone.
    /// </summary>
    private static string? BackgroundZoomAdvice(ScriptBlockKind kind, VisualTransformOptions transform)
    {
        if (kind != ScriptBlockKind.Background || transform.MaxWidth < 1280 || transform.MaxHeight < 720) return null;
        var zoom = Math.Min(transform.MaxWidth / 1280d, transform.MaxHeight / 720d);
        if (transform.MotionOffsetX == 0 && transform.MotionOffsetY == 0)
        {
            var uncovered = transform.MaxWidth < 1280 + 2 * Math.Abs(transform.OffsetX) || transform.MaxHeight < 720 + 2 * Math.Abs(transform.OffsetY);
            if (!uncovered) return null;
            var cover = Math.Ceiling(Math.Max(1 + 2d * Math.Abs(transform.OffsetX) / 1280, 1 + 2d * Math.Abs(transform.OffsetY) / 720) * 20) / 20;
            return cover > BlockDefaults.BackgroundZoomMax
                ? "El fondo está tan desplazado que se verán bordes negros incluso con zoom 300 %."
                : $"Con ese desplazamiento se verán bordes negros: usa zoom {cover * 100:0} % o más para cubrir el cuadro.";
        }
        var needed = LayerGeometry.ZoomToCover(transform.OffsetX, transform.OffsetY, transform.MotionOffsetX, transform.MotionOffsetY);
        if (zoom >= needed - 0.001) return null;
        var suggestion = Math.Ceiling(needed * 20) / 20; // next 5 %
        return suggestion > BlockDefaults.BackgroundZoomMax
            ? "Este paneo es más largo de lo que cubre el zoom máximo: el fondo se ampliará al moverse. Divide el recorrido en varios bloques o usa una imagen más ancha."
            : $"Para que este fondo no cambie de escala al empezar a moverse, usa zoom {suggestion * 100:0} % " +
              $"({Math.Round(1280 * suggestion)}×{Math.Round(720 * suggestion)}) en este bloque y en el fondo anterior.";
    }

    private void ContinueFromPrevious_Click(object sender, RoutedEventArgs e)
    {
        var kind = CurrentEditorKind;
        if (kind is not (ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video))
            return;
        var characterId = (ScriptCharacterCombo.SelectedItem as CharacterChoice)?.Id;
        if (kind == ScriptBlockKind.CharacterShow && characterId is null)
        {
            ScriptStatusText.Text = "Elige primero el personaje: se sigue desde su último render en la escena.";
            return;
        }
        var editing = _editingScriptBlockId is Guid id ? _scriptBlocks.FirstOrDefault(x => x.Id == id) : null;
        var before = editing?.OrderIndex ?? int.MaxValue;
        var previous = _scriptBlocks.Where(x => x.OrderIndex < before && x.Kind == kind &&
                (kind != ScriptBlockKind.CharacterShow || x.CharacterId == characterId))
            .OrderBy(x => x.OrderIndex).LastOrDefault();
        if (previous is null)
        {
            ScriptStatusText.Text = kind == ScriptBlockKind.CharacterShow
                ? "Este personaje no tiene un render anterior en la escena."
                : $"No hay un bloque «{ScriptBlockTypes.First(x => x.Kind == kind).Name}» anterior en la escena.";
            return;
        }
        var end = SceneComposer.VisualTransform(previous);
        VisualWidthBox.Text = end.MaxWidth.ToString(CultureInfo.InvariantCulture);
        VisualHeightBox.Text = end.MaxHeight.ToString(CultureInfo.InvariantCulture);
        VisualOffsetXBox.Text = Math.Clamp(end.OffsetX + end.MotionOffsetX, -1280, 1280).ToString(CultureInfo.InvariantCulture);
        VisualOffsetYBox.Text = Math.Clamp(end.OffsetY + end.MotionOffsetY, -720, 720).ToString(CultureInfo.InvariantCulture);
        var angle = end.RotationDegrees + end.MotionRotationDegrees;
        angle = ((angle + 180) % 360 + 360) % 360 - 180;
        VisualRotationBox.Text = Math.Round(angle, 3).ToString("0.###", CultureInfo.InvariantCulture);
        VisualFlipHorizontalCheck.IsChecked = end.FlipHorizontal;
        VisualFlipVerticalCheck.IsChecked = end.FlipVertical;
        if (kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.Image)
            CharacterPositionCombo.SelectedIndex = SceneComposer.Position(previous) switch { "izquierda" => 0, "derecha" => 2, "auto" => 3, _ => 1 };
        if (kind == ScriptBlockKind.CharacterShow)
            CharacterFramingCombo.SelectedIndex = SceneComposer.FramingPreset(previous) switch
            {
                "auto" => 1, "entero" => 2, "medio" => 3, "detalle" => 4, _ => 0
            };
        // Same picture when nothing was chosen yet (a background or image usually continues with the same file).
        if (kind is ScriptBlockKind.Background or ScriptBlockKind.Image && ScriptAssetCombo.SelectedItem is null && previous.AssetId is Guid asset &&
            ScriptAssetCombo.Items.OfType<AssetChoice>().FirstOrDefault(x => x.Id == asset) is { } same)
            ScriptAssetCombo.SelectedItem = same;
        SyncBackgroundZoomCombo();
        ScriptStatusText.Text = $"Empieza donde terminó el bloque #{previous.OrderIndex + 1} (X {VisualOffsetXBox.Text}, Y {VisualOffsetYBox.Text}, " +
            $"giro {VisualRotationBox.Text}°). Pon ahora el Animar ΔX/ΔY de este tramo y guarda.";
    }
}

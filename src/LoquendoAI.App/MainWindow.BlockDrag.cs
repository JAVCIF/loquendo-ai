using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.App;

/// <summary>
/// «Bloques de escena» (1.4.1): the blocks are reordered by dragging them in the table. One block or several selected
/// ones (they keep their order) are dropped above or below another row; a line on that row shows where they go and
/// the table scrolls near its edges. The ↑/↓ buttons still move them one position.
/// </summary>
public partial class MainWindow
{
    private const string BlockDragFormat = "LoquendoAI.ScriptBlocks";
    private Point? _blockDragStart;
    private ScriptBlockRow? _blockDragPressedRow;
    private bool _blockDragKeepSelection;
    private DataGridRow? _blockDropMarker;

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null and not T)
            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement content => content.Parent,
                _ => null
            };
        return current as T;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent) where T : DependencyObject =>
        FindVisualDescendants<T>(parent).FirstOrDefault();

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var deeper in FindVisualDescendants<T>(child)) yield return deeper;
        }
    }

    private void ScriptBlocksGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _blockDragStart = null;
        _blockDragPressedRow = null;
        _blockDragKeepSelection = false;
        if (FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { Item: ScriptBlockRow row }) return;
        _blockDragStart = e.GetPosition(ScriptBlocksGrid);
        _blockDragPressedRow = row;
        // Pressing on one of several selected rows keeps them all selected, so the group can be dragged together.
        if (Keyboard.Modifiers == ModifierKeys.None && ScriptBlocksGrid.SelectedItems.Count > 1 && ScriptBlocksGrid.SelectedItems.Contains(row))
        {
            _blockDragKeepSelection = true;
            e.Handled = true;
            ScriptBlocksGrid.Focus(); // the grid did not see the click: keep keyboard work (Tab, Supr) on it
        }
    }

    private void ScriptBlocksGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A plain click (no drag) on one of several selected rows selects only that row, as the table normally does.
        if (_blockDragKeepSelection && _blockDragPressedRow is { } row)
        {
            ScriptBlocksGrid.SelectedItems.Clear();
            ScriptBlocksGrid.SelectedItem = row;
        }
        _blockDragStart = null;
        _blockDragPressedRow = null;
        _blockDragKeepSelection = false;
    }

    private void ScriptBlocksGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_blockDragStart is not Point start || _blockDragPressedRow is not { } pressed || e.LeftButton != MouseButtonState.Pressed)
            return;
        var now = e.GetPosition(ScriptBlocksGrid);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var ids = ScriptBlocksGrid.SelectedItems.Cast<ScriptBlockRow>().Select(x => x.Block.Id).ToList();
        if (!ids.Contains(pressed.Block.Id)) ids = [pressed.Block.Id];
        _blockDragStart = null;
        _blockDragKeepSelection = false;
        try
        {
            DragDrop.DoDragDrop(ScriptBlocksGrid, new DataObject(BlockDragFormat, string.Join(";", ids)), DragDropEffects.Move);
        }
        finally
        {
            ClearBlockDropMarker();
            _blockDragPressedRow = null;
        }
    }

    private void ScriptBlocksGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent(BlockDragFormat))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        // Near the top or bottom edge the table scrolls, so a block can travel through a long scene.
        var y = e.GetPosition(ScriptBlocksGrid).Y;
        if (FindVisualDescendant<ScrollViewer>(ScriptBlocksGrid) is { } scroll)
        {
            if (y < 28) scroll.LineUp();
            else if (y > ScriptBlocksGrid.ActualHeight - 28) scroll.LineDown();
        }
        var (insertBefore, row, before) = BlockDropPosition(e);
        if (!ReferenceEquals(row, _blockDropMarker)) ClearBlockDropMarker();
        if (insertBefore is null) e.Effects = DragDropEffects.None;
        if (row is null) return;
        row.BorderThickness = before ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
        row.SetResourceReference(Control.BorderBrushProperty, "Theme.Accent");
        _blockDropMarker = row;
    }

    private void ScriptBlocksGrid_DragLeave(object sender, DragEventArgs e) => ClearBlockDropMarker();

    private async void ScriptBlocksGrid_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var (position, _, _) = BlockDropPosition(e);
        ClearBlockDropMarker();
        if (position is not int insertBefore) return;
        if (e.Data.GetData(BlockDragFormat) is not string text || ScenesList.SelectedItem is not SceneScriptRow sceneRow) return;
        var ids = text.Split(';').Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).ToHashSet();
        var list = _scriptBlocks.OrderBy(x => x.OrderIndex).ToList();
        if (ids.Count == 0 || !list.Any(x => ids.Contains(x.Id))) return;
        var moved = BlockOrder.MoveTo(list, x => ids.Contains(x.Id), insertBefore);
        if (moved.Select(x => x.Id).SequenceEqual(list.Select(x => x.Id))) return;
        await ApplyBlockOrderAsync(sceneRow, moved, ids, $"{ids.Count} bloque(s) movidos a la posición {moved.ToList().FindIndex(x => ids.Contains(x.Id)) + 1}.");
    }

    /// <summary>
    /// Where a drop lands, as the gap in the script order (null = nowhere, e.g. over the scroll bar), with the row
    /// that shows the line and whether the line goes above it. Over a row: above it (upper half) or below it. Above
    /// the first visible row (the column header): before it; below the last visible row: after it.
    /// </summary>
    private (int? InsertBefore, DataGridRow? Row, bool Before) BlockDropPosition(DragEventArgs e)
    {
        var order = _scriptBlocks.OrderBy(x => x.OrderIndex).Select(x => x.Id).ToList();
        int IndexOf(DataGridRow row) => row.Item is ScriptBlockRow item ? order.IndexOf(item.Block.Id) : -1;
        if (FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { } over && IndexOf(over) >= 0)
        {
            var before = e.GetPosition(over).Y < over.ActualHeight / 2;
            return (IndexOf(over) + (before ? 0 : 1), over, before);
        }
        var y = e.GetPosition(ScriptBlocksGrid).Y;
        var visible = FindVisualDescendants<DataGridRow>(ScriptBlocksGrid)
            .Where(row => row.IsVisible && IndexOf(row) >= 0)
            .Select(row => (Row: row, Top: row.TranslatePoint(new Point(0, 0), ScriptBlocksGrid).Y))
            .Where(x => x.Top >= 0 && x.Top < ScriptBlocksGrid.ActualHeight)
            .OrderBy(x => x.Top).ToArray();
        if (visible.Length == 0) return (order.Count, null, false);
        if (y < visible[0].Top) return (IndexOf(visible[0].Row), visible[0].Row, true);
        var last = visible[^1];
        if (y >= last.Top + last.Row.ActualHeight) return (IndexOf(last.Row) + 1, last.Row, false);
        return (null, null, false);
    }

    private void ClearBlockDropMarker()
    {
        if (_blockDropMarker is null) return;
        _blockDropMarker.ClearValue(Control.BorderThicknessProperty);
        _blockDropMarker.ClearValue(Control.BorderBrushProperty);
        _blockDropMarker = null;
    }

    /// <summary>Saves a new order of the scene's blocks, keeps the moved ones selected and reloads timing.</summary>
    private async Task ApplyBlockOrderAsync(SceneScriptRow sceneRow, IReadOnlyList<SceneScriptBlock> ordered,
        IReadOnlySet<Guid> selectedIds, string status)
    {
        if (_currentRepository is null) return;
        var reordered = ordered.Select((x, i) => x with { OrderIndex = i, StartOffsetMs = null }).ToArray();
        try
        {
            await _currentRepository.ReplaceSceneScriptBlocksAsync(sceneRow.Scene.Id, reordered);
            await LoadBlocksAsync(sceneRow.Scene.Id);
            await RefreshSceneTimingAsync(_scriptBlocks);
            _loadingScriptUi = true;
            try
            {
                ScriptBlocksGrid.SelectedItems.Clear();
                foreach (var item in ScriptBlocksGrid.Items.Cast<ScriptBlockRow>().Where(item => selectedIds.Contains(item.Block.Id)))
                    ScriptBlocksGrid.SelectedItems.Add(item);
            }
            finally { _loadingScriptUi = false; }
            if (ScriptBlocksGrid.SelectedItem is ScriptBlockRow first)
            {
                ScriptBlocksGrid.ScrollIntoView(first);
                await LoadBlockIntoEditorAsync(first.Block);
            }
            ScriptStatusText.Text = status;
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }
}

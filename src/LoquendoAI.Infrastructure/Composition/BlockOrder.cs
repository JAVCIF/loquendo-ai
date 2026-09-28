namespace LoquendoAI.Infrastructure.Composition;

/// <summary>Reordering the blocks of a scene (1.4.1: drag and drop in the «Bloques de escena» table).</summary>
public static class BlockOrder
{
    /// <summary>
    /// Moves the selected items, keeping their order, so they land where the gap <paramref name="insertBefore"/>
    /// (0 = before the first item, Count = after the last) is in the ORIGINAL list. Dropping a group inside itself
    /// or right next to where it already is changes nothing.
    /// </summary>
    public static IReadOnlyList<T> MoveTo<T>(IReadOnlyList<T> items, Func<T, bool> selected, int insertBefore)
    {
        insertBefore = Math.Clamp(insertBefore, 0, items.Count);
        var moving = items.Where(selected).ToList();
        if (moving.Count == 0) return items;
        var rest = items.Where(x => !selected(x)).ToList();
        var position = items.Take(insertBefore).Count(x => !selected(x));
        rest.InsertRange(position, moving);
        return rest;
    }
}

using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.Tests;

/// <summary>Drag and drop in «Bloques de escena» (1.4.1): where the dragged blocks land.</summary>
internal static class BlockOrderTests
{
    private static string Move(string items, string selected, int insertBefore) =>
        string.Concat(BlockOrder.MoveTo(items.ToCharArray(), x => selected.Contains(x), insertBefore));

    [Test("Arrastrar bloques: uno o varios (conservan su orden), arriba, abajo, al principio y al final")]
    public static void MoveTo()
    {
        Assert.Equal("BCAD", Move("ABCD", "A", 3), "A antes de D");
        Assert.Equal("ABCD", Move("ABCD", "A", 0), "en su sitio");
        Assert.Equal("ABCD", Move("ABCD", "A", 1), "justo debajo de sí mismo: igual");
        Assert.Equal("DABC", Move("ABCD", "D", 0), "al principio");
        Assert.Equal("BCDA", Move("ABCD", "A", 4), "al final");
        Assert.Equal("BACDE", Move("ABCDE", "B", 0), "sube uno");
        Assert.Equal("BCADE", Move("ABCDE", "AD", 3), "dos separados se juntan en orden donde se sueltan");
        Assert.Equal("BCEAD", Move("ABCDE", "AD", 5), "dos separados al final");
        Assert.Equal("ABCDE", Move("ABCDE", "BC", 2), "soltar dentro del propio grupo no cambia nada");
        Assert.Equal("ABCDE", Move("ABCDE", "", 2), "sin selección");
        Assert.Equal("BCDEA", Move("ABCDE", "A", 99), "índice fuera de rango = final");
    }
}

using System.IO;
using System.Text.Json;
using System.Windows;
using LoquendoAI.Infrastructure.Composition;
using Microsoft.Win32;

namespace LoquendoAI.App;

public partial class MainWindow
{
    /// <summary>Imports the transition list written by scripts/vegas/Listar_transiciones_*.cs so the
    /// block editor and the AI Director offer the plugins installed in this VEGAS (NewBlue, Sapphire…).</summary>
    private void ImportVegasTransitionCatalog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar transiciones de VEGAS (transiciones_vegas.txt)",
            Filter = "Listado de VEGAS (transiciones_vegas.txt)|*.txt|Todos los archivos (*.*)|*.*",
            FileName = "transiciones_vegas.txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var count = VegasTransitionCatalog.ImportInstallationReport(dialog.FileName);
            var selected = VegasPluginCombo.SelectedItem as VegasTransitionChoice;
            VegasPluginCombo.ItemsSource = VegasTransitionCatalog.All;
            if (selected is not null) VegasPluginCombo.SelectedItem = VegasTransitionCatalog.Find(selected.Id);
            AiDirectorStatusText.Text =
                $"Catálogo VEGAS importado: {count:N0} transiciones de tu instalación ({VegasTransitionCatalog.All.Count:N0} en total). " +
                "El Director IA propondrá hasta 12 por borrador. Favoritas opcionales (una por línea, «Nombre | preset»): " +
                VegasTransitionCatalog.FavoritesPath;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            AiDirectorStatusText.Text = "No se pudo importar el catálogo de VEGAS: " + ex.Message;
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Persistence;
using LoquendoAI.Infrastructure.Projects;

namespace LoquendoAI.App;

public partial class MainWindow
{
    /// <summary>One automatic copy of project.db per day, in the background, when a project opens.</summary>
    private static void StartAutomaticBackup(SqliteProjectRepository repository)
    {
        var root = repository.ProjectRoot;
        _ = Task.Run(async () =>
        {
            try { await ProjectBackup.CreateAsync(root, ProjectBackup.AutomaticReason, TimeSpan.FromHours(20)); }
            catch (Exception ex) { ErrorLog.Record(ex); }
        });
    }

    private async void BackupProject_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository)
        {
            StatusText.Text = "Abre un proyecto primero.";
            return;
        }
        try
        {
            StatusText.Text = "Creando copia de seguridad de project.db…";
            var path = await Task.Run(() => ProjectBackup.CreateAsync(repository.ProjectRoot, "manual"));
            StatusText.Text = path is null ? "No hay base de datos que copiar." : "Copia creada: " + path;
            if (path is not null) Process.Start(new ProcessStartInfo(ProjectBackup.Folder(repository.ProjectRoot)) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void CleanProjectCache_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository)
        {
            StatusText.Text = "Abre un proyecto primero.";
            return;
        }
        if (_sceneVoiceGenerationCancellation is not null || _sceneRenderCancellation is not null || _aiRequestCancellation is not null)
        {
            StatusText.Text = "Espera a que terminen la generación de voces, la preview o la IA antes de limpiar.";
            return;
        }
        try
        {
            var root = repository.ProjectRoot;
            var references = await repository.GetScriptAudioReferencesAsync();
            var keep = new[] { _scenePreviewPath }.OfType<string>().ToArray();
            var (plan, vegasBytes) = await Task.Run(() =>
                (ProjectMaintenance.Plan(root, references, keep, DateTime.UtcNow), ProjectMaintenance.VegasExportBytes(root)));
            var vegasNote = vegasBytes > 0
                ? $"\n\nLas exportaciones a VEGAS ocupan {Size(vegasBytes)} en generated\\vegas; no se tocan porque un .veg guardado puede usarlas. Bórralas a mano cuando ya no las necesites."
                : "";
            var pruned = await Task.Run(MediaProbeCache.PruneStale);
            if (plan.Count == 0)
            {
                MessageBox.Show(this, "No hay archivos regenerables sin uso." + vegasNote, "Limpiar caché",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = $"Nada que limpiar ({pruned} mediciones obsoletas descartadas).";
                return;
            }
            var summary = string.Join("\n", plan.GroupBy(x => x.Category)
                .Select(group => $"• {group.Key}: {group.Count()} archivo(s), {Size(group.Sum(x => x.Bytes))}"));
            if (MessageBox.Show(this,
                    $"Se pueden borrar {plan.Count} archivo(s) ({Size(plan.Sum(x => x.Bytes))}):\n\n{summary}\n\n" +
                    "Todo se puede regenerar (preview, «Generar voces»). No se tocan recursos, voces grabadas, exportaciones ni copias de seguridad." +
                    vegasNote + "\n\n¿Borrar?", "Limpiar caché", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            var (deleted, bytes, failed) = await Task.Run(() => ProjectMaintenance.Delete(root, plan));
            StatusText.Text = $"Caché limpia: {deleted} archivo(s), {Size(bytes)} liberados" +
                (failed > 0 ? $"; {failed} en uso o protegidos se dejaron." : ".");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        _ => $"{bytes / 1024d:0} KB"
    };
}

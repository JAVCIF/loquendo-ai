// Ejecútalo desde Herramientas > Scripts > Ejecutar script en VEGAS Pro 12 o 13.
using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Sony.Vegas;
public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        var output = Path.Combine(Path.GetDirectoryName(Sony.Vegas.Script.File), "transiciones_vegas.txt");
        var report = new StringBuilder();
        report.AppendLine("Transiciones y presets disponibles en esta instalación de VEGAS");
        foreach (PlugInNode node in vegas.Transitions) Append(node, report);
        File.WriteAllText(output, report.ToString(), Encoding.UTF8);
        MessageBox.Show("Catálogo guardado en: " + output);
    }
    private static void Append(PlugInNode node, StringBuilder report)
    {
        if (node.IsContainer)
        {
            foreach (PlugInNode child in node) Append(child, report);
            return;
        }
        report.AppendLine(node.Name + " | " + node.UniqueID);
        try
        {
            foreach (EffectPreset preset in node.Presets)
                report.AppendLine("    " + preset.Name);
        }
        catch (Exception ex) { report.AppendLine("    Presets no disponibles: " + ex.Message); }
    }
}

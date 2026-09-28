// Ejecútalo desde Herramientas > Scripts > Ejecutar script en VEGAS Pro 12 o 13, con un proyecto que tenga
// al menos un evento de vídeo (cualquiera). Anota en «efectos_loquendo.txt» (junto a este script):
// - todos los efectos de vídeo instalados que se parecen a los que usa Loquendo AI (de VEGAS y de otros paquetes,
//   como BCC), con su ID, y cuál elige Loquendo AI (siempre el propio de VEGAS);
// - los parámetros y presets del Cortador de galletas y del Desenfoque gaussiano elegidos (los pone un momento en
//   ese evento y los quita).
using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Sony.Vegas;
public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        string output = Path.Combine(Path.GetDirectoryName(Sony.Vegas.Script.File), "efectos_loquendo.txt");
        StringBuilder report = new StringBuilder();
        report.AppendLine("Efectos parecidos instalados (nombre | ID):");
        List(vegas.VideoFX, new string[] { "gauss", "cookie", "galleta" }, report);
        VideoEvent target = null;
        foreach (Track track in vegas.Project.Tracks)
        {
            if (!track.IsVideo()) continue;
            foreach (TrackEvent ev in track.Events) { target = ev as VideoEvent; if (target != null) break; }
            if (target != null) break;
        }
        Inspect(vegas, target, "Cortador de galletas", FindVegasPlugIn(vegas.VideoFX, new string[] { "cookiecutter", "cookie" },
            new string[] { "cortador de galletas", "cookie cutter", "vegas cortador de galletas", "vegas cookie cutter" }), report);
        Inspect(vegas, target, "Desenfoque gaussiano", FindVegasPlugIn(vegas.VideoFX, new string[] { "gaussianblur", "gaussian" },
            new string[] { "desenfoque gaussiano", "gaussian blur", "vegas desenfoque gaussiano", "vegas gaussian blur" }), report);
        File.WriteAllText(output, report.ToString(), Encoding.UTF8);
        MessageBox.Show("Informe guardado en: " + output);
    }
    private static void List(PlugInNode node, string[] words, StringBuilder report)
    {
        foreach (PlugInNode child in node)
        {
            if (child.IsContainer) { List(child, words, report); continue; }
            string text = ((child.Name == null ? "" : child.Name) + "|" + (child.UniqueID == null ? "" : child.UniqueID)).ToLowerInvariant();
            foreach (string w in words) if (text.Contains(w)) { report.AppendLine("    " + child.Name + " | " + child.UniqueID); break; }
        }
    }
    // Same choice as the exported scripts: VEGAS's own OFX effect by ID, else its exact built-in name; never another pack.
    private static PlugInNode FindVegasPlugIn(PlugInNode node, string[] idWords, string[] exactNames)
    {
        PlugInNode found = FindPlugIn(node, idWords, exactNames, true);
        return found != null ? found : FindPlugIn(node, idWords, exactNames, false);
    }
    private static PlugInNode FindPlugIn(PlugInNode node, string[] idWords, string[] exactNames, bool byId)
    {
        foreach (PlugInNode child in node)
        {
            if (child.IsContainer) { PlugInNode found = FindPlugIn(child, idWords, exactNames, byId); if (found != null) return found; continue; }
            string name = (child.Name == null ? "" : child.Name).Trim().ToLowerInvariant();
            string id = (child.UniqueID == null ? "" : child.UniqueID).ToLowerInvariant();
            if (byId)
            {
                if (!id.Contains("vegascreativesoftware") && !id.Contains("sonycreativesoftware")) continue;
                foreach (string w in idWords) if (id.Contains(w)) return child;
            }
            else foreach (string n in exactNames) if (name == n) return child;
        }
        return null;
    }
    private static void Inspect(Vegas vegas, VideoEvent target, string what, PlugInNode plugIn, StringBuilder report)
    {
        report.AppendLine();
        if (plugIn == null) { report.AppendLine(what + ": NO encontrado (el efecto propio de VEGAS no está instalado o tiene otro nombre)."); return; }
        report.AppendLine(what + " elegido: " + plugIn.Name + " | " + plugIn.UniqueID);
        report.AppendLine("Presets:");
        try { foreach (EffectPreset preset in plugIn.Presets) report.AppendLine("    " + preset.Name); }
        catch (Exception ex) { report.AppendLine("    (no se pudieron leer: " + ex.Message + ")"); }
        if (target == null) { report.AppendLine("Parámetros: añade cualquier evento de vídeo al proyecto y vuelve a ejecutar el script."); return; }
        Effect fx = new Effect(plugIn);
        target.Effects.Add(fx);
        try
        {
            report.AppendLine("Parámetros (valores por defecto):");
            Describe(fx.OFXEffect, report);
        }
        catch (Exception ex) { report.AppendLine("Error: " + ex.Message); }
        finally { target.Effects.Remove(fx); }
    }
    private static string Describe(object value)
    {
        if (value == null) return "(nada)";
        string text = value.ToString();
        foreach (string n in new string[] { "R", "G", "B", "A" })
        {
            System.Reflection.FieldInfo f = value.GetType().GetField(n);
            System.Reflection.PropertyInfo p = value.GetType().GetProperty(n);
            if (f != null) text += " " + n + "=" + f.GetValue(value);
            else if (p != null) text += " " + n + "=" + p.GetValue(value, null);
        }
        return text;
    }
    private static void Describe(OFXEffect ofx, StringBuilder report)
    {
        if (ofx == null) { report.AppendLine("    (el efecto no expone parámetros OFX)"); return; }
        foreach (OFXParameter p in ofx.Parameters)
        {
            string line = "    " + p.GetType().Name + " | nombre=" + p.Name + " | etiqueta=" + p.Label;
            try
            {
                System.Reflection.PropertyInfo value = p.GetType().GetProperty("Value");
                if (value != null) line += " | valor=" + Describe(value.GetValue(p, null));
                OFXChoiceParameter c = p as OFXChoiceParameter;
                if (c != null)
                {
                    line += " | opciones=";
                    foreach (OFXChoice o in c.Choices) line += o.Name + "; ";
                }
            }
            catch (Exception ex) { line += " | (" + ex.Message + ")"; }
            report.AppendLine(line);
        }
    }
}

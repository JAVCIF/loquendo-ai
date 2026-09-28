using System.IO;
using System.Text;

namespace LoquendoAI.App;

internal static class ErrorLog
{
    public static string PathName => AppPaths.PathOf("errores.log");

    public static string Record(Exception error)
    {
        var path = PathName;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {error}{Environment.NewLine}{Environment.NewLine}",
                Encoding.UTF8);
            return path;
        }
        catch (IOException) { return "No se pudo escribir errores.log"; }
        catch (UnauthorizedAccessException) { return "No se pudo escribir errores.log"; }
    }

    public static string Summary(Exception error)
    {
        var message = error.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "Error inesperado.";
        return message.Length > 160 ? message[..157] + "…" : message;
    }
}

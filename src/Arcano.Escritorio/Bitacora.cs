using System.IO;

namespace Arcano.Escritorio;

/// <summary>
/// Bitácora local de errores. No registra contenido de los documentos, solo
/// nombres de operación y mensajes técnicos.
/// </summary>
internal static class Bitacora
{
    private static readonly Lock Candado = new();
    private static string Ruta => Path.Combine(App.CarpetaDatos, "bitacora.log");

    public static void Error(string operacion, Exception ex)
    {
        try
        {
            lock (Candado)
            {
                Directory.CreateDirectory(App.CarpetaDatos);
                File.AppendAllText(Ruta, $"{DateTimeOffset.Now:O} [{operacion}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
    }
}

using System;
using System.IO;

namespace Lragrz.Enova.Server.Services;

/// <summary>
/// Audit log: console + %LocalAppData%\eNoVa_tor\logs.
/// Format: [YYYY-MM-DD-HH:MM:SS/ACTION] message {u: userId, d: divisionId, o: orgId}
/// </summary>
public static class AuditLog
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "eNoVa_tor", "logs");

    static AuditLog() => Directory.CreateDirectory(LogDir);

    public static void Write(string action, string message, string? userId = null, string? divisionId = null, string? orgId = null)
    {
        var ids = $"{{u: {userId ?? "-"}, d: {divisionId ?? "-"}, o: {orgId ?? "-"}}}";
        var line = $"[{DateTime.Now:yyyy-MM-dd-HH:mm:ss}/{action}] {message} {ids}";
        Console.WriteLine(line);
        try
        {
            var file = Path.Combine(LogDir, $"{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(file, line + Environment.NewLine);
        }
        catch { /* never crash on logging */ }
    }

    public static void Auth(string message, string? userId = null) => Write("AUTH", message, userId);
    public static void Access(string message, string? userId = null, string? div = null, string? org = null) => Write("ACCESS", message, userId, div, org);
    public static void Action(string message, string? userId = null, string? div = null, string? org = null) => Write("ACTION", message, userId, div, org);
    public static void Config(string message, string? userId = null, string? div = null, string? org = null) => Write("CONFIG", message, userId, div, org);
    public static void Upload(string message, string? userId = null, string? div = null, string? org = null) => Write("UPLOAD", message, userId, div, org);
    public static void Fail(string message, string? userId = null, string? div = null, string? org = null) => Write("FAIL", message, userId, div, org);
    public static void Info(string message) => Write("INFO", message);
}


// Entry point. Double-click (no arguments) -> the GUI.
// With arguments it runs a headless scan, writing the JSON extract and the HTML report:
//
//   TexRay <folder> [output.json] [--fix]

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using TexRay.Core;

namespace TexRay;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0) return RunConsole(args);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    // The exe is a WinExe (no console of its own), so when run from a terminal we
    // re-attach to the parent's console to keep the CLI output visible.
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    static int RunConsole(string[] args)
    {
        AttachConsole(-1);

        bool fix = args.Contains("--fix", StringComparer.OrdinalIgnoreCase);
        var paths = args.Where(a => !a.StartsWith("--")).ToArray();
        if (paths.Length == 0)
        {
            Console.WriteLine("Usage: TexRay <folder> [output.json] [--fix]");
            return 1;
        }

        var folder = paths[0];
        var outPath = paths.Length > 1 ? paths[1] : "extract.json";

        if (!Directory.Exists(folder) && !File.Exists(folder))
        {
            Console.WriteLine($"Folder not found: {folder}");
            return 1;
        }

        var options = new ScanOptions { RootPath = folder };
        options.ApplyMode(ScanMode.FullScan);

        ScanReport report;
        try
        {
            report = ScanEngine.Run(options, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Scan failed: {ex.Message}");
            return 1;
        }

        var violations = ViolationSet.Evaluate(report, options);

        if (fix)
        {
            Console.WriteLine("Fixing texture violations (originals kept as .bak)...");
            var fixReport = TextureFixer.Fix(report, options, null, CancellationToken.None);
            foreach (var a in fixReport.Actions)
                Console.WriteLine($"  fixed {a.File} : {a.Texture}  {a.Before} -> {a.After}");
            foreach (var f in fixReport.Failures)
                Console.WriteLine($"  ! could not fix {f.File}: {f.Error}");
            Console.WriteLine($"  {fixReport.Actions.Count} texture(s) fixed in {fixReport.FilesChanged} file(s), {fixReport.Failures.Count} failure(s)");

            Console.WriteLine("Re-scanning to verify...");
            report = ScanEngine.Run(options, null, CancellationToken.None);
            violations = ViolationSet.Evaluate(report, options);
        }

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, jsonOptions));

        var htmlPath = Path.ChangeExtension(outPath, ".html");
        File.WriteAllText(htmlPath, ReportWriter.Render(report, violations, options));

        Console.WriteLine($"Scanned {violations.FilesScanned} files under {folder}");
        Console.WriteLine($"  YTD count:  {violations.YtdCount} (limit {options.MaxYtd}) {(violations.YtdCountOk ? "OK" : "OVER")}");
        Console.WriteLine($"  Total size: {violations.TotalSizeMb:F1} MB (limit {options.MaxSizeMb} MB) {(violations.SizeOk ? "OK" : "OVER")}");
        Console.WriteLine($"  Violation rows: {violations.RowCount}");
        Console.WriteLine($"Wrote {outPath}");
        Console.WriteLine($"Wrote {htmlPath}");
        return violations.AllClear ? 0 : 2;
    }
}

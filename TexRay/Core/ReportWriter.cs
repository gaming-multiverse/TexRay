// Renders the compliance report as standalone HTML plus a Discord-ready plain-text
// version. English text throughout; the tables mirror the categories posted to creators.

using System.Globalization;
using System.Text;

namespace TexRay.Core;

public static class ReportWriter
{
    public static string Render(ScanReport report, ViolationSet v, ScanOptions options)
    {
        var sb = new StringBuilder();

        sb.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="UTF-8">
            <title>Ped Compliance Report</title>
            <style>
              :root {
                --bg: #130c0c;
                --panel: #1d1313;
                --border: #322222;
                --text: #eae6e6;
                --muted: #9a8f8f;
                --accent: #ff0000;
                --ok: #4caf7d;
                --fail: #ff4d4d;
              }
              * { box-sizing: border-box; }
              body {
                background: var(--bg);
                color: var(--text);
                font-family: -apple-system, "Segoe UI", Roboto, sans-serif;
                max-width: 720px;
                margin: 0 auto;
                padding: 32px 20px 60px;
                line-height: 1.5;
              }
              h1 { font-size: 1.3rem; font-weight: 600; margin: 0 0 4px; }
              .meta { color: var(--muted); font-size: 0.85rem; margin-bottom: 24px; }
              table {
                width: 100%;
                border-collapse: collapse;
                background: var(--panel);
                border: 1px solid var(--border);
                border-radius: 6px;
                overflow: hidden;
                font-size: 0.85rem;
                margin-bottom: 8px;
              }
              th, td { padding: 8px 12px; text-align: left; border-bottom: 1px solid var(--border); }
              th {
                background: #231818;
                color: var(--muted);
                font-weight: 600;
                font-size: 0.75rem;
                text-transform: uppercase;
                letter-spacing: 0.03em;
              }
              td.idx { color: var(--muted); width: 28px; }
              tr:last-child td { border-bottom: none; }
              td { font-family: "SF Mono", Consolas, monospace; font-size: 0.82rem; }
              .section-title { font-size: 0.92rem; margin: 28px 0 10px; color: var(--text); }
              .summary-table td.status { font-weight: 700; font-family: inherit; }
              .summary-table tr.ok td.status { color: var(--ok); }
              .summary-table tr.fail td.status { color: var(--fail); }
              .all-clear { color: var(--ok); font-weight: 600; margin-top: 24px; }
            </style>
            </head>
            <body>
              <h1>Ped Compliance Report</h1>
            """);

        sb.Append($"  <p class=\"meta\">Folder: {Esc(report.Folder)} &middot; Scanned: {report.ScannedAtUtc:yyyy-MM-dd HH:mm} UTC</p>\n");

        sb.Append("  <p class=\"section-title\">Resource limits</p>\n");
        sb.Append("  <table class=\"summary-table\">\n");
        sb.Append("    <thead><tr><th>Check</th><th>Value</th><th>Limit</th><th>Status</th></tr></thead>\n    <tbody>\n");
        SummaryRow(sb, "YTD files (standalone)", v.YtdCount.ToString(), $"max {options.MaxYtd}", v.YtdCountOk);
        SummaryRow(sb, "Total size", $"{v.TotalSizeMb.ToString("F1", CultureInfo.InvariantCulture)} MB",
            $"max {options.MaxSizeMb.ToString("0.#", CultureInfo.InvariantCulture)} MB", v.SizeOk);
        sb.Append("    </tbody>\n  </table>\n");

        Table(sb,
            "Embedded textures that do not match the allowed compression formats",
            new[] { "File", "Texture", "Format" },
            v.EmbeddedCompression.Select(r => new[] { r.File, r.Texture, r.Format }));

        Table(sb,
            "Textures that do not match the allowed compression formats",
            new[] { "File", "Texture", "Format" },
            v.StandaloneCompression.Select(r => new[] { r.File, r.Texture, r.Format }));

        Table(sb,
            $"Embedded textures that exceed {options.MaxResolution}x{options.MaxResolution} resolution",
            new[] { "File", "Texture", "Width", "Height" },
            v.EmbeddedResolution.Select(r => new[] { r.File, r.Texture, r.Width.ToString(), r.Height.ToString() }));

        Table(sb,
            $"Textures that exceed {options.MaxResolution}x{options.MaxResolution} resolution",
            new[] { "File", "Texture", "Width", "Height" },
            v.StandaloneResolution.Select(r => new[] { r.File, r.Texture, r.Width.ToString(), r.Height.ToString() }));

        Table(sb,
            $"Parts that exceed {Num(options.MaxVertices)} vertices / {Num(options.MaxPolygons)} polygons",
            new[] { "File", "Drawable", "Model", "Geometry", "Vertices", "Triangles" },
            v.Geometry.Select(r => new[]
            {
                r.File,
                r.Drawable.Length > 0 ? r.Drawable : r.DrawableIndex.ToString(),
                r.ModelIndex.ToString(), r.GeometryIndex.ToString(),
                Num(r.Vertices), Num(r.Triangles),
            }));

        Table(sb,
            $"Clothing items whose parts TOGETHER exceed {Num(options.MaxVertices)} vertices / {Num(options.MaxPolygons)} polygons",
            new[] { "File", "Item", "Parts", "Vertices", "Triangles" },
            v.ItemTotals.Select(r => new[]
            {
                r.File,
                r.Drawable.Length > 0 ? r.Drawable : $"drawable {r.DrawableIndex}",
                r.Parts.ToString(), Num(r.Vertices), Num(r.Triangles),
            }));

        Table(sb,
            "Files that could not be read (corrupted or unsupported format)",
            new[] { "File", "Error" },
            v.ParseErrors.Select(r => new[] { r.File, r.Error }));

        if (v.AllClear)
            sb.Append("  <p class=\"all-clear\">All files pass the checks.</p>\n");

        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>Plain-text version of the report for pasting straight into Discord —
    /// same section titles, file lists wrapped in code blocks so they align.</summary>
    public static string RenderText(ScanReport report, ViolationSet v, ScanOptions options)
    {
        var sb = new StringBuilder();
        var folderName = Path.GetFileName(report.Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (folderName.Length == 0) folderName = report.Folder;

        sb.AppendLine($"**Ped Compliance Report** - {folderName}");
        sb.AppendLine($"Scanned {v.FilesScanned} files ({v.YddCount} .ydd, {v.YtdCount} .ytd)");
        sb.AppendLine($"YTD: {v.YtdCount} / {options.MaxYtd} {(v.YtdCountOk ? "OK" : "OVER")}  |  " +
                      $"Total size: {v.TotalSizeMb.ToString("F1", CultureInfo.InvariantCulture)} MB / " +
                      $"{options.MaxSizeMb.ToString("0.#", CultureInfo.InvariantCulture)} MB {(v.SizeOk ? "OK" : "OVER")}");

        void Section(string title, IEnumerable<string> lines)
        {
            var list = lines.ToList();
            if (list.Count == 0) return;
            sb.AppendLine();
            sb.AppendLine(title);
            sb.AppendLine("```");
            for (int i = 0; i < list.Count; i++) sb.AppendLine($"{i + 1}. {list[i]}");
            sb.AppendLine("```");
        }

        Section("Embedded textures that do not match the allowed compression formats:",
            v.EmbeddedCompression.Select(r => $"{r.File} - {r.Texture} ({r.Format})"));

        Section("Textures that do not match the allowed compression formats:",
            v.StandaloneCompression.Select(r => $"{r.File} - {r.Texture} ({r.Format})"));

        Section($"Embedded textures that exceed {options.MaxResolution}x{options.MaxResolution} resolution:",
            v.EmbeddedResolution.Select(r => $"{r.File} - {r.Texture} ({r.Width}x{r.Height})"));

        Section($"Textures that exceed {options.MaxResolution}x{options.MaxResolution} resolution:",
            v.StandaloneResolution.Select(r => $"{r.File} - {r.Texture} ({r.Width}x{r.Height})"));

        Section($"Parts that exceed {Num(options.MaxVertices)} vertices / {Num(options.MaxPolygons)} polygons:",
            v.Geometry.Select(r =>
                $"{r.File} - {(r.Drawable.Length > 0 ? r.Drawable : "drawable " + r.DrawableIndex)} " +
                $"({Num(r.Vertices)} verts / {Num(r.Triangles)} tris)"));

        Section($"Clothing items whose parts TOGETHER exceed {Num(options.MaxVertices)} vertices / {Num(options.MaxPolygons)} polygons:",
            v.ItemTotals.Select(r =>
                $"{r.File} - {(r.Drawable.Length > 0 ? r.Drawable : "drawable " + r.DrawableIndex)} " +
                $"(total {Num(r.Vertices)} verts / {Num(r.Triangles)} tris, {r.Parts} parts)"));

        Section("Files that could not be read (corrupted or unsupported format):",
            v.ParseErrors.Select(r => $"{r.File} - {r.Error}"));

        if (v.AllClear)
        {
            sb.AppendLine();
            sb.AppendLine("All files pass the checks.");
        }

        return sb.ToString();
    }

    static void SummaryRow(StringBuilder sb, string label, string actual, string limit, bool ok)
    {
        sb.Append($"    <tr class=\"{(ok ? "ok" : "fail")}\"><td>{Esc(label)}</td><td>{Esc(actual)}</td>" +
                  $"<td>{Esc(limit)}</td><td class=\"status\">{(ok ? "OK" : "OVER")}</td></tr>\n");
    }

    static void Table(StringBuilder sb, string title, string[] columns, IEnumerable<string[]> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return;

        sb.Append("  <div class=\"section\">\n");
        sb.Append($"    <p class=\"section-title\">{Esc(title)}</p>\n    <table>\n      <thead><tr><th>#</th>");
        foreach (var c in columns) sb.Append($"<th>{Esc(c)}</th>");
        sb.Append("</tr></thead>\n      <tbody>\n");
        for (int i = 0; i < list.Count; i++)
        {
            sb.Append($"        <tr><td class=\"idx\">{i + 1}</td>");
            foreach (var cell in list[i]) sb.Append($"<td>{Esc(cell)}</td>");
            sb.Append("</tr>\n");
        }
        sb.Append("      </tbody>\n    </table>\n  </div>\n");
    }

    /// <summary>Thousands separators the English report reader expects, whatever the
    /// machine locale is (a German locale would render 180,185 as "180.185").</summary>
    static string Num(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

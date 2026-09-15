// Flattens a scan report into per-item result rows for a results grid.
// Shared by the WinForms and Avalonia front-ends.

namespace TexRay.Core;

public static class ResultRows
{
    public static IEnumerable<(string Cat, string File, string Item, string Detail, string Status, bool Over)>
        Build(ScanReport report, ScanOptions o)
    {
        var allowed = o.AllowedFormats();

        foreach (var f in report.Files)
        {
            if (f.ParseError != null)
            {
                yield return ("Parse error", f.File, "", f.ParseError, "ERROR", true);
                continue;
            }

            foreach (var tex in f.Textures ?? Enumerable.Empty<TextureInfo>().ToList())
            {
                var flags = TextureFlags(tex, o, allowed, o.CheckStandaloneCompression, o.CheckStandaloneResolution);
                yield return ("YTD texture", f.File, tex.Name,
                    $"{tex.Format}   {tex.Width}x{tex.Height}", flags ?? "OK", flags != null);
            }

            foreach (var tex in f.EmbeddedTextures ?? Enumerable.Empty<TextureInfo>().ToList())
            {
                var flags = TextureFlags(tex, o, allowed, o.CheckEmbeddedCompression, o.CheckEmbeddedResolution);
                yield return ("Embedded texture", f.File, tex.Name,
                    $"{tex.Format}   {tex.Width}x{tex.Height}", flags ?? "OK", flags != null);
            }

            foreach (var g in f.Geometries ?? Enumerable.Empty<GeometryInfo>().ToList())
            {
                var parts = new List<string>(2);
                if (o.CheckVertices && g.VertexCount > o.MaxVertices) parts.Add("VERTICES");
                if (o.CheckPolygons && g.TriangleCount > o.MaxPolygons) parts.Add("POLYGONS");
                var item = g.DrawableName.Length > 0 ? $"{g.DrawableName} [{g.ModelIndex}:{g.GeometryIndex}]"
                                                     : $"drawable {g.DrawableIndex} [{g.ModelIndex}:{g.GeometryIndex}]";
                yield return ("Geometry", f.File, item,
                    $"{g.VertexCount:N0} verts / {g.TriangleCount:N0} tris",
                    parts.Count > 0 ? string.Join(", ", parts) : "OK", parts.Count > 0);
            }

            // whole-item totals (drawables split into several geometries)
            if (!o.CheckItemTotals) continue;
            foreach (var item in (f.Geometries ?? Enumerable.Empty<GeometryInfo>().ToList()).GroupBy(g => g.DrawableIndex))
            {
                if (item.Count() < 2) continue;
                int verts = item.Sum(g => g.VertexCount);
                int tris = item.Sum(g => g.TriangleCount);
                var parts = new List<string>(2);
                if (o.CheckVertices && verts > o.MaxVertices) parts.Add("VERTICES");
                if (o.CheckPolygons && tris > o.MaxPolygons) parts.Add("POLYGONS");
                var name = item.First().DrawableName;
                yield return ("Clothing item", f.File,
                    name.Length > 0 ? name : $"drawable {item.Key}",
                    $"total {verts:N0} verts / {tris:N0} tris  ·  {item.Count()} parts",
                    parts.Count > 0 ? string.Join(", ", parts) : "OK", parts.Count > 0);
            }
        }
    }

    static string? TextureFlags(TextureInfo tex, ScanOptions o, HashSet<string> allowed, bool checkFormat, bool checkResolution)
    {
        var parts = new List<string>(2);
        if (checkFormat && !allowed.Contains(tex.NormalizedFormat)) parts.Add("FORMAT");
        if (checkResolution && (tex.Width > o.MaxResolution || tex.Height > o.MaxResolution)) parts.Add("RESOLUTION");
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}

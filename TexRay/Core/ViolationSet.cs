// Applies the rules in ScanOptions to a ScanReport. The categories mirror the report you
// post: embedded compression, standalone compression, embedded resolution, standalone
// resolution, plus geometry and a resource-limits summary.

namespace TexRay.Core;

public sealed record TextureViolation(string File, string Texture, string Format, int Width, int Height);
public sealed record GeometryViolation(string File, string Drawable, int DrawableIndex, int ModelIndex, int GeometryIndex, int Vertices, int Triangles);
/// <summary>A whole clothing item (drawable) whose parts together exceed the limits.</summary>
public sealed record DrawableViolation(string File, string Drawable, int DrawableIndex, int Parts, int Vertices, int Triangles);
public sealed record ParseFailure(string File, string Error);

public sealed class ViolationSet
{
    public List<TextureViolation> EmbeddedCompression { get; } = new();
    public List<TextureViolation> StandaloneCompression { get; } = new();
    public List<TextureViolation> EmbeddedResolution { get; } = new();
    public List<TextureViolation> StandaloneResolution { get; } = new();
    public List<GeometryViolation> Geometry { get; } = new();
    public List<DrawableViolation> ItemTotals { get; } = new();
    public List<ParseFailure> ParseErrors { get; } = new();

    public long TotalSizeBytes { get; private set; }
    public int YtdCount { get; private set; }
    public int YddCount { get; private set; }
    public int OtherCount { get; private set; }
    public int FilesScanned { get; private set; }

    public double TotalSizeMb => TotalSizeBytes / 1024.0 / 1024.0;

    /// <summary>Names of the files that broke at least one rule.</summary>
    public HashSet<string> OffendingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int RowCount =>
        EmbeddedCompression.Count + StandaloneCompression.Count +
        EmbeddedResolution.Count + StandaloneResolution.Count +
        Geometry.Count + ItemTotals.Count + ParseErrors.Count;

    public bool SizeOk { get; private set; }
    public bool YtdCountOk { get; private set; }
    public bool AllClear => RowCount == 0 && SizeOk && YtdCountOk;

    public static ViolationSet Evaluate(ScanReport report, ScanOptions options)
    {
        var v = new ViolationSet();
        var allowed = options.AllowedFormats();

        foreach (var entry in report.Files)
        {
            v.TotalSizeBytes += entry.SizeBytes;
            v.FilesScanned++;
            if (entry.Type == "ytd") v.YtdCount++;
            else if (entry.Type == "ydd") v.YddCount++;
            else v.OtherCount++; // .yft / .ydr / .ycd / .ymt

            if (entry.ParseError != null)
            {
                v.ParseErrors.Add(new ParseFailure(entry.File, entry.ParseError));
                v.OffendingFiles.Add(entry.File);
                continue;
            }

            foreach (var tex in entry.Textures ?? new List<TextureInfo>())
            {
                if (options.CheckStandaloneCompression && !allowed.Contains(tex.NormalizedFormat))
                {
                    v.StandaloneCompression.Add(Row(entry, tex));
                    v.OffendingFiles.Add(entry.File);
                }
                if (options.CheckStandaloneResolution && Oversized(tex, options.MaxResolution))
                {
                    v.StandaloneResolution.Add(Row(entry, tex));
                    v.OffendingFiles.Add(entry.File);
                }
            }

            foreach (var tex in entry.EmbeddedTextures ?? new List<TextureInfo>())
            {
                if (options.CheckEmbeddedCompression && !allowed.Contains(tex.NormalizedFormat))
                {
                    v.EmbeddedCompression.Add(Row(entry, tex));
                    v.OffendingFiles.Add(entry.File);
                }
                if (options.CheckEmbeddedResolution && Oversized(tex, options.MaxResolution))
                {
                    v.EmbeddedResolution.Add(Row(entry, tex));
                    v.OffendingFiles.Add(entry.File);
                }
            }

            foreach (var geom in entry.Geometries ?? new List<GeometryInfo>())
            {
                bool overVerts = options.CheckVertices && geom.VertexCount > options.MaxVertices;
                bool overTris = options.CheckPolygons && geom.TriangleCount > options.MaxPolygons;
                if (!overVerts && !overTris) continue;

                v.Geometry.Add(new GeometryViolation(entry.File, geom.DrawableName, geom.DrawableIndex,
                    geom.ModelIndex, geom.GeometryIndex, geom.VertexCount, geom.TriangleCount));
                v.OffendingFiles.Add(entry.File);
            }

            // A clothing item is usually split into several geometries, so the whole
            // drawable is checked too. Single-part drawables are already exactly covered
            // by the per-geometry rows above.
            if (!options.CheckItemTotals) continue;
            foreach (var item in (entry.Geometries ?? new List<GeometryInfo>()).GroupBy(g => g.DrawableIndex))
            {
                if (item.Count() < 2) continue;
                int verts = item.Sum(g => g.VertexCount);
                int tris = item.Sum(g => g.TriangleCount);
                bool overVerts = options.CheckVertices && verts > options.MaxVertices;
                bool overTris = options.CheckPolygons && tris > options.MaxPolygons;
                if (!overVerts && !overTris) continue;

                v.ItemTotals.Add(new DrawableViolation(entry.File, item.First().DrawableName,
                    item.Key, item.Count(), verts, tris));
                v.OffendingFiles.Add(entry.File);
            }
        }

        v.SizeOk = v.TotalSizeMb <= options.MaxSizeMb;
        v.YtdCountOk = v.YtdCount <= options.MaxYtd;
        return v;
    }

    static bool Oversized(TextureInfo tex, int max) => tex.Width > max || tex.Height > max;

    static TextureViolation Row(FileResult entry, TextureInfo tex) =>
        new(entry.File, tex.Name, tex.Format, tex.Width, tex.Height);
}

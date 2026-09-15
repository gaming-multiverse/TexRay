// Walks a folder of loose FiveM ped stream files, opens each drawable-carrying file
// (.ydd, .ydr, .yft) and each .ytd with CodeWalker.Core, and records per-texture
// format/resolution and per-geometry vertex/triangle counts. .ycd/.ymt files are
// enumerated so they count toward the size limit, but are never opened.
//
// Files outside the current mode's interest are still enumerated (they count toward the
// size and .ytd-count limits) but are not opened — that's what keeps a single-check scan
// over a whole server resources folder fast.

using CodeWalker.GameFiles;

namespace TexRay.Core;

public readonly record struct ScanProgress(int Done, int Total, string CurrentFile);

public static class ScanEngine
{
    /// <summary>Always scanned.</summary>
    public static readonly string[] Extensions = { ".ydd", ".ytd" };

    /// <summary>Extra types behind the "include .yft/.ydr/.ycd" toggle: .yft/.ydr are
    /// opened and checked like .ydd, .ycd/.ymt only count toward the size limit.</summary>
    public static readonly string[] ExtraExtensions = { ".yft", ".ydr" };
    public static readonly string[] ExtraSizeOnlyExtensions = { ".ycd", ".ymt" };

    /// <summary>Enumerates every relevant file under a folder, or the single file itself
    /// if <paramref name="rootPath"/> points at one.</summary>
    public static List<string> CollectFiles(string rootPath, bool includeExtraTypes)
    {
        var wanted = includeExtraTypes
            ? Extensions.Concat(ExtraExtensions).Concat(ExtraSizeOnlyExtensions).ToArray()
            : Extensions;

        if (File.Exists(rootPath))
        {
            return wanted.Any(e => rootPath.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                ? new List<string> { rootPath }
                : new List<string>();
        }

        if (!Directory.Exists(rootPath)) return new List<string>();

        return Directory.EnumerateFiles(rootPath, "*.*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        })
        .Where(f => wanted.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    public static ScanReport Run(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var files = CollectFiles(options.RootPath, options.IncludeExtraTypes);
        var results = new List<FileResult>(files.Count);

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var path = files[i];
            var name = Path.GetFileName(path);
            progress?.Report(new ScanProgress(i, files.Count, name));

            var ext = Path.GetExtension(path).ToLowerInvariant();
            long sizeBytes;
            try { sizeBytes = new FileInfo(path).Length; }
            catch { sizeBytes = 0; }

            var entry = new FileResult
            {
                File = name,
                FullPath = path,
                Type = ext.TrimStart('.'),
                SizeBytes = sizeBytes,
            };

            bool wanted = ext switch
            {
                ".ytd" => options.NeedsYtd,
                ".ydd" or ".yft" or ".ydr" => options.NeedsYdd,
                _ => false, // .ycd/.ymt: size only
            };
            if (!wanted)
            {
                results.Add(entry);
                continue;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                switch (ext)
                {
                    case ".ytd": ReadYtd(bytes, entry); break;
                    case ".ydd": ReadYdd(bytes, entry, options); break;
                    case ".ydr": ReadYdr(bytes, entry, options); break;
                    case ".yft": ReadYft(bytes, entry, options); break;
                }
                entry.Parsed = true;
            }
            catch (Exception ex)
            {
                entry.ParseError = ex.Message;
            }

            results.Add(entry);
        }

        progress?.Report(new ScanProgress(files.Count, files.Count, ""));

        return new ScanReport
        {
            Folder = options.RootPath,
            ScannedAtUtc = DateTime.UtcNow,
            Files = results,
        };
    }

    static void ReadYtd(byte[] bytes, FileResult entry)
    {
        var ytd = new YtdFile();
        ytd.Load(bytes);
        entry.Textures = ReadTextures(ytd.TextureDict);
    }

    static void ReadYdd(byte[] bytes, FileResult entry, ScanOptions options)
    {
        var ydd = new YddFile();
        ydd.Load(bytes);

        var embedded = new List<TextureInfo>();
        var geometries = new List<GeometryInfo>();

        var drawables = ydd.Drawables ?? Array.Empty<Drawable>();
        for (int di = 0; di < drawables.Length; di++)
        {
            var drawable = drawables[di];
            if (drawable == null) continue;
            ReadDrawable(drawable, drawable.Name ?? $"drawable_{di}", di, options, embedded, geometries);
        }

        Store(entry, options, embedded, geometries);
    }

    static void ReadYdr(byte[] bytes, FileResult entry, ScanOptions options)
    {
        var ydr = new YdrFile();
        ydr.Load(bytes);

        var embedded = new List<TextureInfo>();
        var geometries = new List<GeometryInfo>();
        ReadDrawable(ydr.Drawable, ydr.Drawable?.Name ?? "drawable", 0, options, embedded, geometries);

        Store(entry, options, embedded, geometries);
    }

    static void ReadYft(byte[] bytes, FileResult entry, ScanOptions options)
    {
        var yft = new YftFile();
        yft.Load(bytes);

        var embedded = new List<TextureInfo>();
        var geometries = new List<GeometryInfo>();
        int index = 0;
        foreach (var (drawable, label) in FragDrawables(yft.Fragment))
            ReadDrawable(drawable, label, index++, options, embedded, geometries);

        Store(entry, options, embedded, geometries);
    }

    /// <summary>Every drawable a fragment can carry: the main drawable, the cloth
    /// drawables, the extra drawable array, and the physics-LOD children (deduplicated
    /// by reference). Used by both the scanner and the texture fixer.</summary>
    public static IEnumerable<(DrawableBase Drawable, string Label)> FragDrawables(FragType? frag)
    {
        if (frag == null) yield break;
        var seen = new HashSet<DrawableBase>();

        IEnumerable<(DrawableBase, string)> All()
        {
            if (frag.Drawable != null) yield return (frag.Drawable, frag.Name ?? "fragment");
            if (frag.DrawableCloth != null) yield return (frag.DrawableCloth, "cloth");

            var arr = frag.DrawableArray?.data_items;
            if (arr != null)
                for (int i = 0; i < arr.Length; i++)
                    if (arr[i] != null) yield return (arr[i], $"extra_{i}");

            var cloths = frag.Cloths?.data_items;
            if (cloths != null)
                foreach (var cloth in cloths)
                    if (cloth?.Drawable != null) yield return (cloth.Drawable, "envcloth");

            var group = frag.PhysicsLODGroup;
            foreach (var lod in new[] { group?.PhysicsLOD1, group?.PhysicsLOD2, group?.PhysicsLOD3 })
            {
                var children = lod?.Children?.data_items;
                if (children == null) continue;
                foreach (var child in children)
                {
                    if (child == null) continue;
                    if (child.Drawable1 != null) yield return (child.Drawable1, child.GroupName ?? "child");
                    if (child.Drawable2 != null) yield return (child.Drawable2, (child.GroupName ?? "child") + "_2");
                }
            }
        }

        foreach (var pair in All())
            if (seen.Add(pair.Item1)) yield return pair;
    }

    static void ReadDrawable(DrawableBase? drawable, string name, int index, ScanOptions options,
        List<TextureInfo> embeddedTextures, List<GeometryInfo> geometries)
    {
        if (drawable == null) return;

        if (options.CheckEmbeddedCompression || options.CheckEmbeddedResolution)
        {
            // ShaderGroup.TextureDictionary holds the textures embedded in the file itself.
            var embeddedDict = drawable.ShaderGroup?.TextureDictionary;
            if (embeddedDict != null) embeddedTextures.AddRange(ReadTextures(embeddedDict));
        }

        if (!options.CheckVertices && !options.CheckPolygons) return;

        var models = HighLodModels(drawable);
        for (int mi = 0; mi < models.Length; mi++)
        {
            var geoms = models[mi]?.Geometries ?? Array.Empty<DrawableGeometry>();
            for (int gi = 0; gi < geoms.Length; gi++)
            {
                var geom = geoms[gi];
                if (geom == null) continue;

                var vertexCount = (int)(geom.VertexBuffer?.VertexCount ?? 0);
                var indexCount = (int)(geom.IndexBuffer?.IndicesCount ?? 0);

                geometries.Add(new GeometryInfo
                {
                    DrawableIndex = index,
                    DrawableName = name,
                    ModelIndex = mi,
                    GeometryIndex = gi,
                    VertexCount = vertexCount,
                    TriangleCount = indexCount / 3,
                });
            }
        }
    }

    /// <summary>Only the high-detail models. A drawable also carries Med/Low/VLow LOD
    /// copies of the same mesh, and DrawableBase.AllModels concatenates them all — summing
    /// those reports several times the real mesh size and would not match what OpenIV /
    /// CodeWalker / Blender show. Falls back to the next-best LOD, then to AllModels, so
    /// a drawable is never left unchecked.</summary>
    static DrawableModel[] HighLodModels(DrawableBase drawable)
    {
        var b = drawable.DrawableModels;
        foreach (var lod in new[] { b?.High, b?.Med, b?.Low, b?.VLow })
            if (lod is { Length: > 0 }) return lod;

        return drawable.AllModels ?? Array.Empty<DrawableModel>();
    }

    static void Store(FileResult entry, ScanOptions options,
        List<TextureInfo> embedded, List<GeometryInfo> geometries)
    {
        if (options.CheckEmbeddedCompression || options.CheckEmbeddedResolution)
            entry.EmbeddedTextures = embedded;
        if (options.CheckVertices || options.CheckPolygons)
            entry.Geometries = geometries;
    }

    static List<TextureInfo> ReadTextures(TextureDictionary? dict)
    {
        var list = new List<TextureInfo>();
        var items = dict?.Textures?.data_items;
        if (items == null) return list;

        foreach (var tex in items)
        {
            if (tex == null) continue;
            list.Add(new TextureInfo
            {
                Name = tex.Name ?? "(unnamed)",
                Width = tex.Width,
                Height = tex.Height,
                Format = tex.Format.ToString(), // e.g. D3DFMT_DXT1 / D3DFMT_DXT5 / D3DFMT_A8R8G8B8
                MipLevels = tex.Levels,
            });
        }
        return list;
    }
}

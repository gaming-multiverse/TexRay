// Data model for a scan. The JSON property names are snake_case so the exported extract
// stays readable and stable for anything that consumes it outside the app.

using System.Text.Json.Serialization;

namespace TexRay.Core;

public sealed class ScanReport
{
    [JsonPropertyName("folder")] public string Folder { get; set; } = "";
    [JsonPropertyName("scanned_at_utc")] public DateTime ScannedAtUtc { get; set; }
    [JsonPropertyName("files")] public List<FileResult> Files { get; set; } = new();

    [JsonIgnore] public long TotalSizeBytes => Files.Sum(f => f.SizeBytes);
    [JsonIgnore] public int YtdCount => Files.Count(f => f.Type == "ytd");
    [JsonIgnore] public int YddCount => Files.Count(f => f.Type == "ydd");
}

public sealed class FileResult
{
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = ""; // "ytd" or "ydd"
    [JsonPropertyName("size_bytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("parse_error")] public string? ParseError { get; set; }

    // ytd
    [JsonPropertyName("textures")] public List<TextureInfo>? Textures { get; set; }

    // ydd
    [JsonPropertyName("embedded_textures")] public List<TextureInfo>? EmbeddedTextures { get; set; }
    [JsonPropertyName("geometries")] public List<GeometryInfo>? Geometries { get; set; }

    /// <summary>Full path on disk. Not serialised — the report is meant to be shareable.</summary>
    [JsonIgnore] public string FullPath { get; set; } = "";

    /// <summary>False when the current mode didn't need this file opened (it still counts
    /// toward the size/count totals, it just has no texture or geometry detail).</summary>
    [JsonIgnore] public bool Parsed { get; set; }

    [JsonIgnore]
    public IEnumerable<TextureInfo> AllTextures =>
        (Textures ?? Enumerable.Empty<TextureInfo>()).Concat(EmbeddedTextures ?? Enumerable.Empty<TextureInfo>());
}

public sealed class TextureInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("mip_levels")] public int MipLevels { get; set; }

    /// <summary>"D3DFMT_DXT5" -> "DXT5", for comparing against the allowed-format list.</summary>
    [JsonIgnore] public string NormalizedFormat => Format.Replace("D3DFMT_", "").ToUpperInvariant();
}

public sealed class GeometryInfo
{
    [JsonPropertyName("drawable_index")] public int DrawableIndex { get; set; }
    [JsonPropertyName("model_index")] public int ModelIndex { get; set; }
    [JsonPropertyName("geometry_index")] public int GeometryIndex { get; set; }
    [JsonPropertyName("vertex_count")] public int VertexCount { get; set; }
    [JsonPropertyName("triangle_count")] public int TriangleCount { get; set; }

    [JsonIgnore] public string DrawableName { get; set; } = "";
}

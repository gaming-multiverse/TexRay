// The rules a scan runs under, plus the mode presets that drive which of them are on.

namespace TexRay.Core;

public enum ScanMode
{
    /// <summary>Geometry vertex / polygon counts inside .ydd drawables.</summary>
    CheckerYdd,
    /// <summary>Compression format of textures embedded inside .ydd files.</summary>
    EmbeddedCompression,
    /// <summary>Compression format of textures in standalone .ytd files.</summary>
    TextureCompression,
    /// <summary>Resolution of textures embedded inside .ydd files.</summary>
    EmbeddedResolution,
    /// <summary>Resolution of textures in standalone .ytd files.</summary>
    YtdResolution,
    /// <summary>Everything above in one pass.</summary>
    FullScan,
}

public sealed class ScanOptions
{
    public ScanMode Mode { get; set; } = ScanMode.FullScan;
    public string RootPath { get; set; } = "";

    // Geometry
    public bool CheckVertices { get; set; }
    public int MaxVertices { get; set; } = 60000;
    public bool CheckPolygons { get; set; }
    public int MaxPolygons { get; set; } = 60000;

    // Compression
    public bool CheckEmbeddedCompression { get; set; }
    public bool CheckStandaloneCompression { get; set; }
    public string AllowedFormatsRaw { get; set; } = "DXT1,DXT5,BC7";

    // Resolution
    public bool CheckEmbeddedResolution { get; set; }
    public bool CheckStandaloneResolution { get; set; }
    public int MaxResolution { get; set; } = 2048;

    // Resource limits (reported as a summary, never as per-file rows)
    public int MaxYtd { get; set; } = 200;
    public double MaxSizeMb { get; set; } = 200;

    /// <summary>Also scan .yft/.ydr (geometry + embedded textures) and count .ycd/.ymt
    /// toward the size limit. Off = classic .ydd/.ytd-only behaviour.</summary>
    public bool IncludeExtraTypes { get; set; } = true;

    /// <summary>Also check each clothing item's TOTAL vertices/polygons across all its
    /// parts, not just each part on its own — a jacket split into three 25k pieces is still
    /// a 75k item. Standard option, on by default.</summary>
    public bool CheckItemTotals { get; set; } = true;

    /// <summary>Whether drawable-carrying files (.ydd, and .yft/.ydr when included) must be opened.</summary>
    public bool NeedsYdd => CheckVertices || CheckPolygons || CheckEmbeddedCompression || CheckEmbeddedResolution;
    public bool NeedsYtd => CheckStandaloneCompression || CheckStandaloneResolution;

    public HashSet<string> AllowedFormats() =>
        AllowedFormatsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToUpperInvariant())
            .ToHashSet();

    /// <summary>Turns the checks belonging to <paramref name="mode"/> on and the rest off.
    /// Thresholds are left alone so the user's numbers survive a mode switch.</summary>
    public void ApplyMode(ScanMode mode)
    {
        Mode = mode;
        CheckVertices = CheckPolygons = false;
        CheckEmbeddedCompression = CheckStandaloneCompression = false;
        CheckEmbeddedResolution = CheckStandaloneResolution = false;

        switch (mode)
        {
            case ScanMode.CheckerYdd:
                CheckVertices = CheckPolygons = true;
                break;
            case ScanMode.EmbeddedCompression:
                CheckEmbeddedCompression = true;
                break;
            case ScanMode.TextureCompression:
                CheckStandaloneCompression = true;
                break;
            case ScanMode.EmbeddedResolution:
                CheckEmbeddedResolution = true;
                break;
            case ScanMode.YtdResolution:
                CheckStandaloneResolution = true;
                break;
            case ScanMode.FullScan:
                CheckVertices = CheckPolygons = true;
                CheckEmbeddedCompression = CheckStandaloneCompression = true;
                CheckEmbeddedResolution = CheckStandaloneResolution = true;
                break;
        }
    }

    public static string Title(ScanMode mode) => mode switch
    {
        ScanMode.CheckerYdd => "CheckerYDD",
        ScanMode.EmbeddedCompression => "Embedded Compression",
        ScanMode.TextureCompression => "Texture Compression",
        ScanMode.EmbeddedResolution => "Texture Resolution Scanner",
        ScanMode.YtdResolution => "Resolution Scanner 2 (YTD)",
        ScanMode.FullScan => "Full Scan",
        _ => mode.ToString(),
    };

    public static string Subtitle(ScanMode mode) => mode switch
    {
        ScanMode.CheckerYdd => "Vertex and polygon counts per geometry inside .ydd / .yft / .ydr drawables",
        ScanMode.EmbeddedCompression => "Compression format of textures embedded in .ydd / .yft / .ydr files",
        ScanMode.TextureCompression => "Compression format of textures in standalone .ytd files",
        ScanMode.EmbeddedResolution => "Resolution of textures embedded in .ydd / .yft / .ydr files",
        ScanMode.YtdResolution => "Resolution of textures in standalone .ytd files",
        ScanMode.FullScan => "Every check in one pass — geometry, compression and resolution",
        _ => "",
    };
}

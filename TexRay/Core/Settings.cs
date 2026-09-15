// Remembers the last scanned folder and the thresholds between launches.
// Stored at %AppData%\TexRay-GamingMultiverse\settings.json — kept apart from the
// MazeCreations build so the two versions never overwrite each other's thresholds.

using System.Text.Json;

namespace TexRay.Core;

public sealed class AppSettings
{
    public string LastPath { get; set; } = "";
    public int MaxVertices { get; set; } = 60000;
    public int MaxPolygons { get; set; } = 60000;
    public int MaxResolution { get; set; } = 2048;
    public string AllowedFormats { get; set; } = "DXT1,DXT5,BC7";
    public int MaxYtd { get; set; } = 200;
    public double MaxSizeMb { get; set; } = 200;
    public bool IncludeExtraTypes { get; set; } = true;
    /// <summary>Check each clothing item's total vertices/polygons across its parts.</summary>
    public bool CheckItemTotals { get; set; } = true;

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TexRay-GamingMultiverse", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // A corrupted settings file must never stop the app — fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Saving is best-effort; a read-only profile shouldn't crash a scan.
        }
    }
}

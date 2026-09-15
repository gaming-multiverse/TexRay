// Fixes texture violations in place: downscales oversized textures to the limit and
// re-encodes disallowed compression formats to DXT1 (opaque) / DXT5 (alpha), for both
// standalone .ytd files and dictionaries embedded in .ydd drawables.
//
// The original file is kept next to the fixed one as "<name>.bak" (never overwritten by
// a later fix, so the very first original always survives). A fixed file is only written
// after the rebuilt bytes successfully re-parse.
//
// Geometry violations (vertices/polygons) can NOT be auto-fixed — decimating a rigged,
// skinned mesh needs manual work in Blender/3ds Max.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace TexRay.Core;

public sealed record FixAction(string File, string Texture, string Before, string After);
public sealed record FixFailure(string File, string Error);

public sealed class FixReport
{
    public List<FixAction> Actions { get; } = new();
    public List<FixFailure> Failures { get; } = new();
    public int FilesChanged { get; set; }
}

public static class TextureFixer
{
    public static FixReport Fix(ScanReport report, ScanOptions options,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var result = new FixReport();
        var allowed = options.AllowedFormats();

        var candidates = report.Files.Where(f =>
            f.ParseError == null && f.FullPath.Length > 0 &&
            (HasFixable(f.Textures, options.CheckStandaloneCompression, options.CheckStandaloneResolution, options, allowed) ||
             HasFixable(f.EmbeddedTextures, options.CheckEmbeddedCompression, options.CheckEmbeddedResolution, options, allowed)))
            .ToList();

        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = candidates[i];
            progress?.Report(new ScanProgress(i, candidates.Count, entry.File));
            try
            {
                if (FixFile(entry, options, allowed, result)) result.FilesChanged++;
            }
            catch (Exception ex)
            {
                result.Failures.Add(new FixFailure(entry.File, ex.Message));
            }
        }

        progress?.Report(new ScanProgress(candidates.Count, candidates.Count, ""));
        return result;
    }

    static bool HasFixable(List<TextureInfo>? textures, bool checkFmt, bool checkRes,
        ScanOptions o, HashSet<string> allowed)
    {
        if (textures == null || (!checkFmt && !checkRes)) return false;
        return textures.Any(t =>
            (checkFmt && !allowed.Contains(t.NormalizedFormat)) ||
            (checkRes && (t.Width > o.MaxResolution || t.Height > o.MaxResolution)));
    }

    static bool FixFile(FileResult entry, ScanOptions o, HashSet<string> allowed, FixReport result)
    {
        var originalBytes = File.ReadAllBytes(entry.FullPath);
        bool changed = false;
        byte[] newBytes;

        bool FixEmbedded(TextureDictionary? dict) => FixDictionary(dict, entry.File, o, allowed,
            o.CheckEmbeddedCompression, o.CheckEmbeddedResolution, result);

        switch (entry.Type)
        {
            case "ytd":
            {
                var ytd = new YtdFile();
                ytd.Load(originalBytes);
                changed = FixDictionary(ytd.TextureDict, entry.File, o, allowed,
                    o.CheckStandaloneCompression, o.CheckStandaloneResolution, result);
                if (!changed) return false;
                newBytes = ytd.Save();
                new YtdFile().Load(newBytes); // must re-parse before we dare overwrite
                break;
            }
            case "ydd":
            {
                var ydd = new YddFile();
                ydd.Load(originalBytes);
                foreach (var drawable in ydd.Drawables ?? Array.Empty<Drawable>())
                    changed |= FixEmbedded(drawable?.ShaderGroup?.TextureDictionary);
                if (!changed) return false;
                newBytes = ydd.Save();
                new YddFile().Load(newBytes);
                break;
            }
            case "ydr":
            {
                var ydr = new YdrFile();
                ydr.Load(originalBytes);
                changed = FixEmbedded(ydr.Drawable?.ShaderGroup?.TextureDictionary);
                if (!changed) return false;
                newBytes = ydr.Save();
                new YdrFile().Load(newBytes);
                break;
            }
            case "yft":
            {
                var yft = new YftFile();
                yft.Load(originalBytes);
                foreach (var (drawable, _) in ScanEngine.FragDrawables(yft.Fragment))
                    changed |= FixEmbedded(drawable.ShaderGroup?.TextureDictionary);
                if (!changed) return false;
                newBytes = yft.Save();
                new YftFile().Load(newBytes);
                break;
            }
            default:
                return false; // .ycd/.ymt carry no textures
        }

        var bak = entry.FullPath + ".bak";
        if (!File.Exists(bak)) File.WriteAllBytes(bak, originalBytes);
        File.WriteAllBytes(entry.FullPath, newBytes);
        return true;
    }

    static bool FixDictionary(TextureDictionary? dict, string fileName, ScanOptions o,
        HashSet<string> allowed, bool checkFmt, bool checkRes, FixReport result)
    {
        var items = dict?.Textures?.data_items;
        if (items == null || (!checkFmt && !checkRes)) return false;

        var list = new List<Texture>(items.Length);
        bool changed = false;

        foreach (var tex in items)
        {
            if (tex == null) continue;

            var norm = Normalize(tex.Format);
            bool badFormat = checkFmt && !allowed.Contains(norm);
            bool oversized = checkRes && (tex.Width > o.MaxResolution || tex.Height > o.MaxResolution);
            if (!badFormat && !oversized)
            {
                list.Add(tex);
                continue;
            }

            var before = $"{tex.Format} {tex.Width}x{tex.Height}";
            try
            {
                var fixedTex = RebuildTexture(tex, o.MaxResolution, badFormat, allowed);
                list.Add(fixedTex);
                changed = true;
                result.Actions.Add(new FixAction(fileName, tex.Name ?? "(unnamed)", before,
                    $"{fixedTex.Format} {fixedTex.Width}x{fixedTex.Height}"));
            }
            catch (Exception ex)
            {
                // keep the original texture so the rest of the file can still be fixed
                list.Add(tex);
                result.Failures.Add(new FixFailure(fileName, $"{tex.Name}: {ex.Message}"));
            }
        }

        if (changed) dict!.BuildFromTextureList(list);
        return changed;
    }

    // ------------------------------------------------------------- rebuild one texture

    static Texture RebuildTexture(Texture tex, int maxRes, bool badFormat, HashSet<string> allowed)
    {
        if (tex.Depth > 1)
            throw new NotSupportedException("volume/array textures are not supported by auto-fix");

        int w = tex.Width, h = tex.Height;
        var rgba = DecodeTopMip(tex, w, h);

        // resize down to the limit (and to block-aligned dimensions for BC encoding)
        int nw = w, nh = h;
        if (w > maxRes || h > maxRes)
        {
            double s = (double)maxRes / Math.Max(w, h);
            nw = MultipleOf4((int)Math.Round(w * s));
            nh = MultipleOf4((int)Math.Round(h * s));
        }
        else
        {
            nw = MultipleOf4(w);
            nh = MultipleOf4(h);
        }
        if (nw != w || nh != h)
        {
            rgba = Resize(rgba, w, h, nw, nh);
            w = nw; h = nh;
        }

        var target = PickTarget(Normalize(tex.Format), badFormat, allowed, rgba);

        var encoder = new BcEncoder();
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.MaxMipMapLevel = MipCount(w, h);
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.OutputOptions.Format = target;
        encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;

        var dds = encoder.EncodeToDds(rgba, w, h, BCnEncoder.Encoder.PixelFormat.Rgba32);
        using var ms = new MemoryStream();
        dds.Write(ms);

        // let CodeWalker's own DDS importer build the GTA texture (stride, mips, layout),
        // then restore this texture's identity
        var newTex = DDSIO.GetTexture(ms.ToArray());
        newTex.Name = tex.Name;
        newTex.NameHash = tex.NameHash;
        newTex.Usage = tex.Usage;
        newTex.UsageFlags = tex.UsageFlags;
        return newTex;
    }

    static string Normalize(TextureFormat fmt) =>
        fmt.ToString().Replace("D3DFMT_", "").ToUpperInvariant();

    static CompressionFormat PickTarget(string sourceFormat, bool badFormat, HashSet<string> allowed, byte[] rgba)
    {
        if (!badFormat)
        {
            // only oversized: keep the (already allowed) format
            return sourceFormat switch
            {
                "DXT1" => HasTransparency(rgba) ? CompressionFormat.Bc1WithAlpha : CompressionFormat.Bc1,
                "DXT5" => CompressionFormat.Bc3,
                "BC7" => CompressionFormat.Bc7,
                _ => CompressionFormat.Bc3,
            };
        }

        bool alpha = HasTransparency(rgba);
        if (!alpha && allowed.Contains("DXT1")) return CompressionFormat.Bc1;
        if (allowed.Contains("DXT5")) return CompressionFormat.Bc3;
        if (allowed.Contains("BC7")) return CompressionFormat.Bc7;
        if (allowed.Contains("DXT1")) return alpha ? CompressionFormat.Bc1WithAlpha : CompressionFormat.Bc1;
        return CompressionFormat.Bc3;
    }

    static bool HasTransparency(byte[] rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4)
            if (rgba[i] < 250) return true;
        return false;
    }

    static int MultipleOf4(int v) => Math.Max(4, v & ~3);

    /// <summary>Mip levels down to a 4x4 smallest mip (BC block size).</summary>
    static int MipCount(int w, int h)
    {
        int levels = 1;
        while ((w >> levels) >= 4 && (h >> levels) >= 4 && levels < 14) levels++;
        return levels;
    }

    // -------------------------------------------------------------------- decoding

    static byte[] DecodeTopMip(Texture tex, int w, int h)
    {
        var full = tex.Data?.FullData ?? throw new InvalidDataException("texture has no pixel data");
        var norm = Normalize(tex.Format);

        CompressionFormat? bc = norm switch
        {
            "DXT1" => CompressionFormat.Bc1WithAlpha,
            "DXT3" => CompressionFormat.Bc2,
            "DXT5" => CompressionFormat.Bc3,
            "ATI1" => CompressionFormat.Bc4,
            "ATI2" => CompressionFormat.Bc5,
            "BC7" => CompressionFormat.Bc7,
            _ => null,
        };

        if (bc != null)
        {
            int blockBytes = bc is CompressionFormat.Bc1 or CompressionFormat.Bc1WithAlpha or CompressionFormat.Bc4 ? 8 : 16;
            int len = ((w + 3) / 4) * ((h + 3) / 4) * blockBytes;
            if (full.Length < len) throw new InvalidDataException("texture data shorter than expected");
            var mip0 = full.AsSpan(0, len).ToArray();
            var colors = new BcDecoder().DecodeRaw(mip0, w, h, bc.Value);
            return ToRgbaBytes(colors);
        }

        int pixels = w * h;
        switch (norm)
        {
            case "A8R8G8B8": // stored little-endian: B,G,R,A per pixel
            {
                Require(full, pixels * 4);
                var outB = new byte[pixels * 4];
                for (int i = 0; i < pixels; i++)
                {
                    outB[i * 4 + 0] = full[i * 4 + 2];
                    outB[i * 4 + 1] = full[i * 4 + 1];
                    outB[i * 4 + 2] = full[i * 4 + 0];
                    outB[i * 4 + 3] = full[i * 4 + 3];
                }
                return outB;
            }
            case "A8": // alpha-only; mirror the value into RGB so either channel samples right
            {
                Require(full, pixels);
                var outB = new byte[pixels * 4];
                for (int i = 0; i < pixels; i++)
                {
                    outB[i * 4 + 0] = outB[i * 4 + 1] = outB[i * 4 + 2] = full[i];
                    outB[i * 4 + 3] = full[i];
                }
                return outB;
            }
            case "L8": // luminance
            {
                Require(full, pixels);
                var outB = new byte[pixels * 4];
                for (int i = 0; i < pixels; i++)
                {
                    outB[i * 4 + 0] = outB[i * 4 + 1] = outB[i * 4 + 2] = full[i];
                    outB[i * 4 + 3] = 255;
                }
                return outB;
            }
            default:
                throw new NotSupportedException($"decoding {tex.Format} is not supported by auto-fix");
        }
    }

    static void Require(byte[] data, int length)
    {
        if (data.Length < length) throw new InvalidDataException("texture data shorter than expected");
    }

    static byte[] ToRgbaBytes(ColorRgba32[] colors)
    {
        var outB = new byte[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            outB[i * 4 + 0] = colors[i].r;
            outB[i * 4 + 1] = colors[i].g;
            outB[i * 4 + 2] = colors[i].b;
            outB[i * 4 + 3] = colors[i].a;
        }
        return outB;
    }

    // -------------------------------------------------------------------- resizing

    static byte[] Resize(byte[] rgba, int w, int h, int nw, int nh)
    {
        using var src = new Bitmap(w, h, GdiPixelFormat.Format32bppArgb);
        CopyIntoBitmap(rgba, src, w, h);

        using var dst = new Bitmap(nw, nh, GdiPixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            g.DrawImage(src, new Rectangle(0, 0, nw, nh), 0, 0, w, h, GraphicsUnit.Pixel);
        }

        return CopyOutOfBitmap(dst, nw, nh);
    }

    static void CopyIntoBitmap(byte[] rgba, Bitmap bmp, int w, int h)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, GdiPixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int s = (y * w + x) * 4;
                    row[x * 4 + 0] = rgba[s + 2]; // GDI+ stores BGRA
                    row[x * 4 + 1] = rgba[s + 1];
                    row[x * 4 + 2] = rgba[s + 0];
                    row[x * 4 + 3] = rgba[s + 3];
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, w * 4);
            }
        }
        finally { bmp.UnlockBits(data); }
    }

    static byte[] CopyOutOfBitmap(Bitmap bmp, int w, int h)
    {
        var rgba = new byte[w * h * 4];
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, GdiPixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w * 4);
                for (int x = 0; x < w; x++)
                {
                    int d = (y * w + x) * 4;
                    rgba[d + 0] = row[x * 4 + 2];
                    rgba[d + 1] = row[x * 4 + 1];
                    rgba[d + 2] = row[x * 4 + 0];
                    rgba[d + 3] = row[x * 4 + 3];
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return rgba;
    }
}

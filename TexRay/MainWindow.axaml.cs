// Avalonia front-end for TexRay. All scan/fix/report logic lives in the shared
// core files (linked from the parent folder); this file is only presentation — the same
// role MainForm.cs plays in the WinForms app.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TexRay.Core;

namespace TexRay;

public sealed record ResultRow(string Category, string File, string Item, string Detail, string Status);

public partial class MainWindow : Window
{
    static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#60BE8C"));
    static readonly IBrush FailBrush = new SolidColorBrush(Color.Parse("#FF4D4D"));

    static string AppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    readonly ScanOptions _options = new();
    AppSettings _settings = new();
    ScanReport? _report;
    ViolationSet? _violations;
    CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();

        BtnModeYdd.Click += (_, _) => EnterMode(ScanMode.CheckerYdd);
        BtnModeEmbComp.Click += (_, _) => EnterMode(ScanMode.EmbeddedCompression);
        BtnModeTexComp.Click += (_, _) => EnterMode(ScanMode.TextureCompression);
        BtnModeEmbRes.Click += (_, _) => EnterMode(ScanMode.EmbeddedResolution);
        BtnModeYtdRes.Click += (_, _) => EnterMode(ScanMode.YtdResolution);
        BtnModeFull.Click += (_, _) => EnterMode(ScanMode.FullScan);
        BtnInfo.Click += async (_, _) => await Dialogs.InfoAsync(this, "Info",
            $"TexRay - GamingMultiverse\n\n" +
            $"Version {AppVersion}\n" +
            $"Developer: perseuslive_\n\n" +
            "FiveM ped compliance scanner — checks creator peds against the server rules " +
            "and auto-fixes texture violations.");
        HomeSubtitle.Text = $"FiveM ped compliance scanner  ·  v{AppVersion}";

        BtnBackScan.Click += (_, _) => ShowView(HomeView);
        BtnBackResults.Click += (_, _) => ShowView(ScanView);
        BtnSearch.Click += async (_, _) => await PickFolderAsync();
        BtnScan.Click += (_, _) => RunScan();
        BtnCopy.Click += async (_, _) => await CopyTextAsync();
        BtnFix.Click += async (_, _) => await FixTexturesAsync();
        BtnExportHtml.Click += async (_, _) => await ExportHtmlAsync();
        BtnExportJson.Click += async (_, _) => await ExportJsonAsync();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        _settings = AppSettings.Load();
        ApplySettings();
        Closing += (_, _) => SaveSettings();
    }

    void ShowView(Control view)
    {
        HomeView.IsVisible = view == HomeView;
        ScanView.IsVisible = view == ScanView;
        ResultsView.IsVisible = view == ResultsView;
    }

    // ------------------------------------------------------------- persistence

    void ApplySettings()
    {
        PathBox.Text = _settings.LastPath;
        NumVertices.Value = ClampTo(NumVertices, _settings.MaxVertices);
        NumPolygons.Value = ClampTo(NumPolygons, _settings.MaxPolygons);
        NumResolution.Value = ClampTo(NumResolution, _settings.MaxResolution);
        NumMaxYtd.Value = ClampTo(NumMaxYtd, _settings.MaxYtd);
        NumMaxSize.Value = ClampTo(NumMaxSize, (decimal)_settings.MaxSizeMb);
        if (_settings.AllowedFormats.Trim().Length > 0) FormatsBox.Text = _settings.AllowedFormats;
        ChkExtraTypes.IsChecked = _settings.IncludeExtraTypes;
        ChkItemTotals.IsChecked = _settings.CheckItemTotals;
    }

    static decimal ClampTo(NumericUpDown n, decimal value) => Math.Clamp(value, n.Minimum, n.Maximum);

    void SaveSettings()
    {
        _settings.LastPath = PathBox.Text?.Trim() ?? "";
        _settings.MaxVertices = (int)(NumVertices.Value ?? 60000);
        _settings.MaxPolygons = (int)(NumPolygons.Value ?? 60000);
        _settings.MaxResolution = (int)(NumResolution.Value ?? 2048);
        _settings.AllowedFormats = FormatsBox.Text?.Trim() ?? "DXT1,DXT5,BC7";
        _settings.MaxYtd = (int)(NumMaxYtd.Value ?? 200);
        _settings.MaxSizeMb = (double)(NumMaxSize.Value ?? 200);
        _settings.IncludeExtraTypes = ChkExtraTypes.IsChecked ?? true;
        _settings.CheckItemTotals = ChkItemTotals.IsChecked ?? true;
        _settings.Save();
    }

    // -------------------------------------------------------------- drag & drop

    void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p != null);
        if (path == null) return;

        PathBox.Text = path;
        if (HomeView.IsVisible) EnterMode(ScanMode.FullScan);
        else if (ResultsView.IsVisible) ShowView(ScanView);
    }

    // ------------------------------------------------------------------- scan

    void EnterMode(ScanMode mode)
    {
        _options.ApplyMode(mode);
        ModeTitle.Text = ScanOptions.Title(mode);
        ModeSubtitle.Text = ScanOptions.Subtitle(mode);

        bool geo = _options.CheckVertices || _options.CheckPolygons;
        RowGeoV.IsVisible = RowGeoP.IsVisible = geo;
        ChkItemTotals.IsVisible = geo;
        if (geo)
        {
            ChkVertices.IsChecked = _options.CheckVertices;
            ChkPolygons.IsChecked = _options.CheckPolygons;
        }
        RowRes.IsVisible = _options.CheckEmbeddedResolution || _options.CheckStandaloneResolution;
        RowFmt.IsVisible = _options.CheckEmbeddedCompression || _options.CheckStandaloneCompression;
        RowLimits.IsVisible = mode == ScanMode.FullScan;

        Progress.Value = 0;
        StatusText.Text = "";
        ShowView(ScanView);
    }

    async Task PickFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick the ped / resources folder to scan",
            AllowMultiple = false,
        });
        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (path != null) PathBox.Text = path;
    }

    void PullOptionsFromUi()
    {
        if (RowGeoV.IsVisible)
        {
            _options.CheckVertices = ChkVertices.IsChecked ?? false;
            _options.CheckPolygons = ChkPolygons.IsChecked ?? false;
        }
        _options.MaxVertices = (int)(NumVertices.Value ?? 60000);
        _options.MaxPolygons = (int)(NumPolygons.Value ?? 60000);
        _options.MaxResolution = (int)(NumResolution.Value ?? 2048);
        _options.AllowedFormatsRaw = FormatsBox.Text ?? "DXT1,DXT5,BC7";
        _options.MaxYtd = (int)(NumMaxYtd.Value ?? 200);
        _options.MaxSizeMb = (double)(NumMaxSize.Value ?? 200);
        _options.IncludeExtraTypes = ChkExtraTypes.IsChecked ?? true;
        _options.CheckItemTotals = ChkItemTotals.IsChecked ?? true;
    }

    async void RunScan()
    {
        if (_cts != null) { _cts.Cancel(); return; }

        var root = PathBox.Text?.Trim().Trim('"') ?? "";
        if (root.Length == 0 || (!Directory.Exists(root) && !File.Exists(root)))
        {
            await Dialogs.InfoAsync(this, "No folder", "Pick a folder first (SEARCH) or paste a valid path.");
            return;
        }

        PullOptionsFromUi();
        _options.RootPath = root;
        SaveSettings();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        BtnScan.Content = "CANCEL";

        var progress = new Progress<ScanProgress>(p =>
        {
            Progress.Maximum = Math.Max(1, p.Total);
            Progress.Value = Math.Min(p.Done, p.Total);
            StatusText.Text = p.CurrentFile.Length > 0 ? $"{p.Done + 1} / {p.Total}   {p.CurrentFile}" : $"{p.Total} files";
        });

        try
        {
            _report = await Task.Run(() => ScanEngine.Run(_options, progress, token), token);
            _violations = ViolationSet.Evaluate(_report, _options);
            ShowResults();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            await Dialogs.InfoAsync(this, "Scan failed", ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            BtnScan.Content = "SCAN";
        }
    }

    // ----------------------------------------------------------------- results

    void ShowResults()
    {
        if (_report == null || _violations == null) return;
        var v = _violations;

        ResultsTitle.Text = ScanOptions.Title(_options.Mode);

        var otherPart = v.OtherCount > 0 ? $", {v.OtherCount} other" : "";
        SummaryText.Text =
            $"Scanned {v.FilesScanned} files  ({v.YddCount} .ydd, {v.YtdCount} .ytd{otherPart})  in  {_report.Folder}\n" +
            $"YTD files:  {v.YtdCount} / {_options.MaxYtd}   {(v.YtdCountOk ? "OK" : "OVER LIMIT")}        " +
            $"Total size:  {v.TotalSizeMb:F1} MB / {_options.MaxSizeMb:0.#} MB   {(v.SizeOk ? "OK" : "OVER LIMIT")}";

        int broken = v.RowCount + (v.SizeOk ? 0 : 1) + (v.YtdCountOk ? 0 : 1);
        if (v.AllClear)
        {
            VerdictText.Text = "ALL CLEAR ✓";
            VerdictText.Foreground = OkBrush;
        }
        else
        {
            VerdictText.Text = $"{broken} VIOLATION{(broken == 1 ? "" : "S")}";
            VerdictText.Foreground = FailBrush;
        }

        // only the files that break a rule are listed — clean files stay out of the grid
        const int maxRows = 20000;
        var rows = new List<ResultRow>();
        foreach (var r in ResultRows.Build(_report, _options))
        {
            if (!r.Over) continue;
            rows.Add(new ResultRow(r.Cat, r.File, r.Item, r.Detail, r.Status));
            if (rows.Count >= maxRows) break;
        }
        ResultsGrid.ItemsSource = rows;

        if (rows.Count >= maxRows)
            SummaryText.Text += $"   (showing first {maxRows:N0} rows)";

        ShowView(ResultsView);
    }

    // ------------------------------------------------------------------- fixing

    async Task FixTexturesAsync()
    {
        if (_report == null || _violations == null || _cts != null) return;

        int fixable = _violations.EmbeddedCompression.Count + _violations.StandaloneCompression.Count +
                      _violations.EmbeddedResolution.Count + _violations.StandaloneResolution.Count;
        if (fixable == 0)
        {
            await Dialogs.InfoAsync(this, "Nothing to fix",
                "No fixable texture violations.\n\nGeometry violations (vertices/polygons) and unreadable " +
                "files cannot be fixed automatically — those need the creator to rework the model.");
            return;
        }

        var confirmed = await Dialogs.ConfirmAsync(this, "Fix textures",
            $"Fix {fixable} texture violation(s)?\n\n" +
            $"•  Oversized textures are downscaled to {_options.MaxResolution}px\n" +
            $"•  Disallowed formats are re-encoded to DXT1 (opaque) / DXT5 (alpha)\n" +
            $"•  Every original file is kept next to the fixed one as \".bak\"\n\n" +
            "Geometry violations and unreadable files are left untouched.");
        if (!confirmed) return;

        BtnFix.IsEnabled = false;
        BtnCopy.IsEnabled = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<ScanProgress>(p =>
        {
            VerdictText.Foreground = OkBrush;
            VerdictText.Text = p.CurrentFile.Length > 0 ? $"FIXING {p.Done + 1}/{p.Total}  {p.CurrentFile}" : "FIX DONE";
        });

        FixReport? fixReport = null;
        try
        {
            var report = _report;
            fixReport = await Task.Run(() => TextureFixer.Fix(report, _options, progress, token), token);
        }
        catch (Exception ex)
        {
            await Dialogs.InfoAsync(this, "Fix failed", ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            BtnFix.IsEnabled = true;
            BtnCopy.IsEnabled = true;
        }

        if (fixReport == null) return;

        var msg = $"Fixed {fixReport.Actions.Count} texture(s) in {fixReport.FilesChanged} file(s).";
        if (fixReport.Failures.Count > 0)
            msg += $"\n\n{fixReport.Failures.Count} could not be fixed:\n" +
                   string.Join("\n", fixReport.Failures.Take(8).Select(f => $"•  {f.File}: {f.Error}"));
        msg += "\n\nRe-scanning now to verify.";
        await Dialogs.InfoAsync(this, "Fix complete", msg);

        RunScan();
    }

    // ------------------------------------------------------------------ exports

    async Task CopyTextAsync()
    {
        if (_report == null || _violations == null || Clipboard == null) return;

        await Clipboard.SetTextAsync(ReportWriter.RenderText(_report, _violations, _options));
        BtnCopy.Content = "COPIED ✓";
        await Task.Delay(1500);
        BtnCopy.Content = "COPY TEXT";
    }

    async Task ExportHtmlAsync()
    {
        if (_report == null || _violations == null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "ped_report.html",
            DefaultExtension = "html",
            FileTypeChoices = new[] { new FilePickerFileType("HTML report") { Patterns = new[] { "*.html" } } },
        });
        var path = file?.TryGetLocalPath();
        if (path == null) return;

        File.WriteAllText(path, ReportWriter.Render(_report, _violations, _options));
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    async Task ExportJsonAsync()
    {
        if (_report == null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "extract.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });
        var path = file?.TryGetLocalPath();
        if (path == null) return;

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(_report, jsonOptions));
    }
}

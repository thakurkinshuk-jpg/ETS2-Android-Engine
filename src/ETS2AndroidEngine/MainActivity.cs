using Android.App;
using Android.Content;
using Android.Opengl;
using Android.OS;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;

namespace ETS2AndroidEngine;

[Activity(Label = "ETS2 Android Engine", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    private const int PickArchiveRequest = 1001;
    private const int ExportTextureRequest = 1002;

    private TextView _status = null!;
    private TextView _report = null!;

    private Button _selectButton = null!;
    private Button _renderButton = null!;
    private Button _copyButton = null!;
    private Button _exportButton = null!;
    private Button _scanButton = null!;

    private PmgV20Model? _model;
    private DecodedDdsTexture? _color;
    private DecodedDdsTexture? _glass;
    private DecodedDdsTexture? _lightmask;
    private DecodedDdsTexture? _environment;

    private byte[]? _colorDds;

    private Android.Net.Uri? _archiveUri;

    private GLSurfaceView? _glView;
    private string _diagnosticReport = "";

    private static readonly string[] MaterialPaths =
    {
        "/automat/86/86b2c2e652897f77.mat",
        "/automat/b3/b3a48013e515444f.mat",
        "/automat/9f/9f2d80ad77b3c72a.mat",
        "/automat/5d/5d99dcfbd26bebf0.mat",
        "/automat/f0/f0d34c6670855c56.mat",
        "/automat/84/841f0c93e373bad4.mat",
        "/automat/cf/cf0a056cbdd16884.mat",
        "/automat/da/da4d9d4cbfbf1693.mat",
        "/automat/3b/3be06047c43a8a4d.mat",
        "/automat/ec/ecc5854f240fb7d9.mat"
    };

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var layout = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        layout.SetPadding(20, 20, 20, 20);

        layout.AddView(new TextView(this)
        {
            Text = "ETS2 ANDROID ENGINE\n\nDAF XF MATERIAL ANALYSIS",
            TextSize = 21
        });

        _selectButton = new Button(this)
        {
            Text = "SELECT ETS2 BASE.SCS"
        };

        _renderButton = new Button(this)
        {
            Text = "VIEW DAF XF TRUCK",
            Enabled = false
        };

        _copyButton = new Button(this)
        {
            Text = "COPY FULL MATERIAL REPORT",
            Enabled = false
        };

        _exportButton = new Button(this)
        {
            Text = "EXPORT ORIGINAL COLOR.DDS",
            Enabled = false
        };

        _scanButton = new Button(this)
        {
            Text = "SCAN DAF PAINTJOB FILES",
            Enabled = false
        };

        _status = new TextView(this)
        {
            Text = "Select your original ETS2 base.scs archive.",
            TextSize = 15
        };

        _report = new TextView(this)
        {
            Text = "Model, textures and material definitions will appear here.",
            TextSize = 12
        };

        var scroll = new ScrollView(this);
        scroll.AddView(_report);

        _selectButton.Click += (_, _) => SelectArchive();
        _renderButton.Click += (_, _) => ShowTruck();
        _copyButton.Click += (_, _) => CopyReport();
        _exportButton.Click += (_, _) => ExportColorTexture();
        _scanButton.Click += (_, _) => ScanPaintAssets();

        layout.AddView(_selectButton);
        layout.AddView(_renderButton);
        layout.AddView(_copyButton);
        layout.AddView(_exportButton);
        layout.AddView(_scanButton);
        layout.AddView(_status);
        layout.AddView(scroll);

        SetContentView(layout);
    }

    private void SelectArchive()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");

        StartActivityForResult(intent, PickArchiveRequest);
    }

    private void ExportColorTexture()
    {
        if (_colorDds == null)
        {
            Toast.MakeText(
                this,
                "Load base.scs first.",
                ToastLength.Short)!.Show();

            return;
        }

        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/octet-stream");
        intent.PutExtra(Intent.ExtraTitle, "daf_xf_color.dds");

        StartActivityForResult(intent, ExportTextureRequest);
    }

    protected override async void OnActivityResult(
        int requestCode,
        Result resultCode,
        Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (resultCode != Result.Ok || data?.Data == null)
            return;

        if (requestCode == ExportTextureRequest)
        {
            try
            {
                using var output =
                    ContentResolver!.OpenOutputStream(data.Data)
                    ?? throw new IOException("Cannot create output file.");

                await output.WriteAsync(_colorDds!);
                await output.FlushAsync();

                _status.Text = "COLOR.DDS EXPORTED SUCCESSFULLY";

                Toast.MakeText(
                    this,
                    "Original color.dds saved!",
                    ToastLength.Long)!.Show();
            }
            catch (Exception ex)
            {
                _status.Text = "EXPORT FAILED\n" + ex.Message;
            }

            return;
        }

        if (requestCode != PickArchiveRequest)
            return;

        var uri = data.Data;
        _archiveUri = uri;

        _selectButton.Enabled = false;
        _renderButton.Enabled = false;
        _copyButton.Enabled = false;
        _exportButton.Enabled = false;
        _scanButton.Enabled = false;

        _status.Text = "Reading DAF XF geometry, textures and materials...";
        _report.Text = "";

        try
        {
            var loaded = await Task.Run(() =>
            {
                using var input =
                    ContentResolver!.OpenInputStream(uri)
                    ?? throw new IOException("Cannot open archive.");

                using var reader = HashFsReader.Open(input);

                byte[] Extract(string path)
                {
                    var files = reader.Extract(path);

                    if (files.Length == 0)
                        throw new FileNotFoundException(
                            "Asset not found: " + path);

                    return files[0];
                }

                byte[] colorBytes =
                    Extract("/vehicle/truck/daf_xf/color.dds");

                var model = PmgV20Reader.Read(
                    Extract("/vehicle/truck/daf_xf/truck.pmg"));

                var color = DdsTextureDecoder.Decode(colorBytes);

                var glass = DdsTextureDecoder.Decode(
                    Extract("/vehicle/truck/share/glass_ex.dds"));

                var lightmask = DdsTextureDecoder.Decode(
                    Extract("/vehicle/truck/daf_xf/lightmask.dds"));

                var environment = DdsTextureDecoder.Decode(
                    Extract("/material/environment/generic_s.dds"));

                var report = new StringBuilder();

                report.AppendLine("ETS2 DAF XF MODEL REPORT");
                report.AppendLine("------------------------");
                report.AppendLine($"Pieces: {model.PieceCount}");
                report.AppendLine($"Parts: {model.PartCount}");
                report.AppendLine($"Bones: {model.BoneCount}");

                report.AppendLine();
                report.AppendLine($"COLOR: {color.Width} x {color.Height}");
                report.AppendLine($"GLASS: {glass.Width} x {glass.Height}");
                report.AppendLine($"LIGHTMASK: {lightmask.Width} x {lightmask.Height}");
                report.AppendLine($"ENVIRONMENT: {environment.Width} x {environment.Height}");

                report.AppendLine();
                report.AppendLine("MATERIAL COUNTS");

                for (int material = 0; material <= 9; material++)
                {
                    int count = 0;

                    foreach (var piece in model.Pieces)
                    {
                        if (piece.MaterialIndex == material)
                            count++;
                    }

                    report.AppendLine(
                        $"Material {material}: {count} pieces");
                }

                report.AppendLine();
                report.AppendLine("UV CHANNEL DIAGNOSTICS");

                for (int material = 0; material <= 9; material++)
                {
                    report.AppendLine();
                    report.AppendLine($"MATERIAL {material}");

                    foreach (var piece in model.Pieces)
                    {
                        if (piece.MaterialIndex != material)
                            continue;

                        report.AppendLine(
                            $"Piece {piece.Index}: vertices={piece.VertexCount}, UV channels={piece.TexCoordWidth}");

                        for (int channel = 0;
                             channel < piece.TexCoordWidth;
                             channel++)
                        {
                            float minU = float.MaxValue;
                            float maxU = float.MinValue;
                            float minV = float.MaxValue;
                            float maxV = float.MinValue;

                            int valid = 0;

                            foreach (var vertex in piece.Vertices)
                            {
                                if (vertex.TextureCoordinates.Length <= channel)
                                    continue;

                                var uv = vertex.TextureCoordinates[channel];

                                if (float.IsNaN(uv.X) ||
                                    float.IsNaN(uv.Y) ||
                                    float.IsInfinity(uv.X) ||
                                    float.IsInfinity(uv.Y))
                                    continue;

                                minU = Math.Min(minU, uv.X);
                                maxU = Math.Max(maxU, uv.X);
                                minV = Math.Min(minV, uv.Y);
                                maxV = Math.Max(maxV, uv.Y);
                                valid++;
                            }

                            if (valid > 0)
                            {
                                report.AppendLine(
                                    $"  UV{channel}: U={minU:F3}..{maxU:F3}, V={minV:F3}..{maxV:F3}");
                            }
                        }
                    }
                }

                report.AppendLine();
                report.AppendLine("DAF XF MATERIAL DEFINITIONS");
                report.AppendLine("============================");

                for (int i = 0; i < MaterialPaths.Length; i++)
                {
                    string path = MaterialPaths[i];

                    report.AppendLine();
                    report.AppendLine($"MATERIAL INDEX {i}");
                    report.AppendLine(path);
                    report.AppendLine("----------------------------");

                    try
                    {
                        byte[] bytes = Extract(path);
                        string materialText =
                            Encoding.UTF8.GetString(bytes);

                        report.AppendLine(materialText);
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine(
                            "MATERIAL READ ERROR: " + ex.Message);
                    }
                }

                report.AppendLine();
                report.AppendLine("FOUR TEXTURES DECODED SUCCESSFULLY.");
                report.AppendLine($"Original color.dds size: {colorBytes.Length} bytes");

                return (
                    Model: model,
                    Color: color,
                    Glass: glass,
                    Lightmask: lightmask,
                    Environment: environment,
                    ColorBytes: colorBytes,
                    Report: report.ToString()
                );
            });

            _model = loaded.Model;
            _color = loaded.Color;
            _glass = loaded.Glass;
            _lightmask = loaded.Lightmask;
            _environment = loaded.Environment;
            _colorDds = loaded.ColorBytes;

            _diagnosticReport = loaded.Report;
            _report.Text = _diagnosticReport;

            _status.Text = "MODEL, TEXTURES AND MATERIALS ANALYZED";

            _renderButton.Enabled = true;
            _copyButton.Enabled = true;
            _exportButton.Enabled = true;
            _scanButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _status.Text = "LOADING FAILED\n" + ex.Message;
        }
        finally
        {
            _selectButton.Enabled = true;
        }
    }

    private async void ScanPaintAssets()
    {
        if (_archiveUri == null)
            return;

        var uri = _archiveUri;

        _selectButton.Enabled = false;
        _renderButton.Enabled = false;
        _copyButton.Enabled = false;
        _exportButton.Enabled = false;
        _scanButton.Enabled = false;

        _status.Text = "Scanning base.scs for DAF paintjob files...";

        try
        {
            string scanReport = await Task.Run(() =>
            {
                using var input =
                    ContentResolver!.OpenInputStream(uri)
                    ?? throw new IOException("Cannot reopen base.scs.");

                using var reader = HashFsReader.Open(input);

                return BuildPaintAssetReport(reader);
            });

            _diagnosticReport +=
                "\n\n" + scanReport;

            _report.Text = _diagnosticReport;

            _status.Text = "PAINTJOB ASSET SCAN FINISHED";

            Toast.MakeText(
                this,
                "Paintjob scan completed. Copy the report.",
                ToastLength.Long)!.Show();
        }
        catch (Exception ex)
        {
            _status.Text = "PAINTJOB SCAN FAILED\n" + ex.Message;
        }
        finally
        {
            _selectButton.Enabled = true;
            _renderButton.Enabled = _model != null;
            _copyButton.Enabled = !string.IsNullOrWhiteSpace(_diagnosticReport);
            _exportButton.Enabled = _colorDds != null;
            _scanButton.Enabled = _archiveUri != null;
        }
    }

    private static string BuildPaintAssetReport(
        IHashFsReader reader)
    {
        var report = new StringBuilder();

        report.AppendLine("DAF XF PAINTJOB ASSET SCAN");
        report.AppendLine("==========================");

        report.AppendLine(
            "Searching DAF vehicle assets and truck definition directories.");

        var roots = new[]
        {
            "/def/vehicle/truck/",
            "/vehicle/truck/daf_xf/",
            "/def/vehicle/truck/daf_xf/"
        };

        var pending = new Stack<(string Path, int Depth)>();

        foreach (string root in roots)
            pending.Push((root, 0));

        var visitedDirectories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var candidateFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int directoriesScanned = 0;
        int directoryErrors = 0;

        const int MaxDirectories = 4000;
        const int MaxDepth = 10;
        const int MaxFilesToDisplay = 350;

        while (pending.Count > 0 &&
               directoriesScanned < MaxDirectories)
        {
            var current = pending.Pop();

            string directoryPath =
                current.Path.TrimEnd('/') + "/";

            if (!visitedDirectories.Add(directoryPath))
                continue;

            try
            {
                var listing = reader.GetDirectoryListing(
                    directoryPath,
                    false,
                    true);

                directoriesScanned++;

                foreach (string file in listing.Files)
                {
                    string fullPath =
                        ResolveArchivePath(directoryPath, file);

                    string extension =
                        Path.GetExtension(fullPath).ToLowerInvariant();

                    bool usefulExtension =
                        extension == ".sii" ||
                        extension == ".sui" ||
                        extension == ".mat" ||
                        extension == ".tobj" ||
                        extension == ".dds";

                    bool isDafAsset =
                        fullPath.StartsWith(
                            "/vehicle/truck/daf_xf/",
                            StringComparison.OrdinalIgnoreCase);

                    bool isTruckDefinition =
                        fullPath.StartsWith(
                            "/def/vehicle/truck/",
                            StringComparison.OrdinalIgnoreCase);

                    bool mentionsDafOrPaint =
                        fullPath.IndexOf(
                            "daf",
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fullPath.IndexOf(
                            "paint",
                            StringComparison.OrdinalIgnoreCase) >= 0;

                    if (usefulExtension &&
                        (isDafAsset || (isTruckDefinition && mentionsDafOrPaint)))
                    {
                        candidateFiles.Add(fullPath);
                    }
                }

                if (current.Depth >= MaxDepth)
                    continue;

                foreach (string subdirectory in listing.Subdirectories)
                {
                    string fullDirectory =
                        ResolveArchivePath(directoryPath, subdirectory)
                        .TrimEnd('/') + "/";

                    if (!visitedDirectories.Contains(fullDirectory))
                    {
                        pending.Push((
                            fullDirectory,
                            current.Depth + 1));
                    }
                }
            }
            catch
            {
                directoryErrors++;
            }
        }

        report.AppendLine();
        report.AppendLine($"Directories scanned: {directoriesScanned}");
        report.AppendLine($"Directory listing errors: {directoryErrors}");
        report.AppendLine($"Candidate files found: {candidateFiles.Count}");

        report.AppendLine();
        report.AppendLine("CANDIDATE FILE PATHS");
        report.AppendLine("-------------------");

        var orderedFiles = candidateFiles
            .OrderBy(
                path => path,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        int displayed = 0;

        foreach (string path in orderedFiles)
        {
            if (displayed >= MaxFilesToDisplay)
                break;

            report.AppendLine(path);
            displayed++;
        }

        if (candidateFiles.Count > MaxFilesToDisplay)
        {
            report.AppendLine();
            report.AppendLine(
                $"Output limited to {MaxFilesToDisplay} paths.");
        }

        report.AppendLine();
        report.AppendLine("PAINT / TEXTURE REFERENCES IN TEXT FILES");
        report.AppendLine("----------------------------------------");

        int textFilesRead = 0;
        int matchingTextFiles = 0;

        foreach (string path in orderedFiles)
        {
            string extension =
                Path.GetExtension(path).ToLowerInvariant();

            
            if (extension != ".sii" &&
                extension != ".sui" &&
                extension != ".mat")
                continue;

            if (textFilesRead >= 120)
                break;

            textFilesRead++;

            try
            {
                var extracted = reader.Extract(path);

                if (extracted.Length == 0)
                    continue;

                string content =
                    Encoding.UTF8.GetString(extracted[0]);

                var matchingLines = new List<string>();

                foreach (string line in content.Split('\n'))
                {
                    bool relevant =
                        line.IndexOf("paint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("texture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("daf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("truck", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("accessory", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (relevant)
                    {
                        string trimmed = line.Trim();

                        if (trimmed.Length > 220)
                            trimmed = trimmed.Substring(0, 220);

                        matchingLines.Add(trimmed);

                        if (matchingLines.Count >= 12)
                            break;
                    }
                }

                if (matchingLines.Count == 0)
                    continue;

                matchingTextFiles++;

                report.AppendLine();
                report.AppendLine("FILE: " + path);

                foreach (string line in matchingLines)
                    report.AppendLine("  " + line);
            }
            catch (Exception ex)
            {
                report.AppendLine();
                report.AppendLine(
                    "TEXT READ ERROR: " + path + " : " + ex.Message);
            }
        }

        report.AppendLine();
        report.AppendLine($"Text files inspected: {textFilesRead}");
        report.AppendLine($"Files with relevant references: {matchingTextFiles}");

        report.AppendLine();
        report.AppendLine("SCAN COMPLETE.");

        if (candidateFiles.Count == 0)
        {
            report.AppendLine(
                "No candidate paths were found. This archive may not expose directory listings for the scanned folders.");
        }

        return report.ToString();
    }

    private static string ResolveArchivePath(
        string parent,
        string child)
    {
        if (child.StartsWith("/", StringComparison.Ordinal))
            return child;

        return parent.TrimEnd('/') + "/" + child.TrimStart('/');
    }

    private void ShowTruck()
    {
        if (_model == null ||
            _color == null ||
            _glass == null ||
            _lightmask == null ||
            _environment == null)
        {
            Toast.MakeText(
                this,
                "Load the model and textures first.",
                ToastLength.Short)!.Show();

            return;
        }

        _glView = new GLSurfaceView(this);
        _glView.SetEGLContextClientVersion(2);

        _glView.SetRenderer(new DafXfRenderer(
            _model,
            _color,
            _glass,
            _lightmask,
            _environment));

        _glView.RenderMode = Rendermode.Continuously;

        SetContentView(_glView);
    }

    private void CopyReport()
    {
        if (string.IsNullOrWhiteSpace(_diagnosticReport))
            return;

        var clipboard =
            (ClipboardManager)GetSystemService(
                ClipboardService)!;

        clipboard.PrimaryClip = ClipData.NewPlainText(
            "ETS2 DAF XF full material and paintjob report",
            _diagnosticReport);

        Toast.MakeText(
            this,
            "Full report copied!",
            ToastLength.Short)!.Show();
    }

    protected override void OnPause()
    {
        _glView?.OnPause();
        base.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _glView?.OnResume();
    }
}

      

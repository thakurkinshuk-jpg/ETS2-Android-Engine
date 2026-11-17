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
    private const int PickBaseRequest = 1001;
    private const int ExportTextureRequest = 1002;
    private const int PickDefRequest = 1003;

    private TextView _status = null!;
    private TextView _report = null!;
    private Button _selectButton = null!;
    private Button _selectDefButton = null!;
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
    private Android.Net.Uri? _defArchiveUri;
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
            Text = "SELECT BASE.SCS"
        };

        _selectDefButton = new Button(this)
        {
            Text = "SELECT DEF.SCS"
        };

        _renderButton = new Button(this)
        {
            Text = "VIEW DAF XF TRUCK",
            Enabled = false
        };

        _copyButton = new Button(this)
        {
            Text = "COPY FULL REPORT",
            Enabled = false
        };

        _exportButton = new Button(this)
        {
            Text = "EXPORT ORIGINAL COLOR.DDS",
            Enabled = false
        };

        _scanButton = new Button(this)
        {
            Text = "SCAN DEF.SCS PAINTJOB DATA",
            Enabled = false
        };

        _status = new TextView(this)
        {
            Text = "Select base.scs and then select def.scs.",
            TextSize = 15
        };

        _report = new TextView(this)
        {
            Text = "Model, textures, materials and paintjob definitions will appear here.",
            TextSize = 12
        };

        var scroll = new ScrollView(this);
        scroll.AddView(_report);

        _selectButton.Click += (_, _) => SelectArchive(PickBaseRequest);
        _selectDefButton.Click += (_, _) => SelectArchive(PickDefRequest);
        _renderButton.Click += (_, _) => ShowTruck();
        _copyButton.Click += (_, _) => CopyReport();
        _exportButton.Click += (_, _) => ExportColorTexture();
        _scanButton.Click += (_, _) => ScanPaintAssets();

        layout.AddView(_selectButton);
        layout.AddView(_selectDefButton);
        layout.AddView(_renderButton);
        layout.AddView(_copyButton);
        layout.AddView(_exportButton);
        layout.AddView(_scanButton);
        layout.AddView(_status);
        layout.AddView(scroll);

        SetContentView(layout);
    }

    private void SelectArchive(int requestCode)
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        StartActivityForResult(intent, requestCode);
    }

    private void ExportColorTexture()
    {
        if (_colorDds == null)
        {
            Toast.MakeText(
                this, "Load base.scs first.", ToastLength.Short)!.Show();
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
                    this, "Original color.dds saved!",
                    ToastLength.Long)!.Show();
            }
            catch (Exception ex)
            {
                _status.Text = "EXPORT FAILED\n" + ex.Message;
            }
            return;
        }

        if (requestCode == PickDefRequest)
        {
            _defArchiveUri = data.Data;
            _scanButton.Enabled = true;
            _status.Text =
                "DEF.SCS selected. Tap SCAN DEF.SCS PAINTJOB DATA.";

            Toast.MakeText(
                this, "def.scs selected.", ToastLength.Short)!.Show();
            return;
        }

        if (requestCode != PickBaseRequest)
            return;

        var uri = data.Data;
        _archiveUri = uri;

        SetBusy(true);
        _status.Text =
            "Reading DAF XF geometry, textures and materials...";
        _report.Text = "";

        try
        {
            var loaded = await Task.Run(() =>
            {
                using var input =
                    ContentResolver!.OpenInputStream(uri)
                    ?? throw new IOException("Cannot open base.scs.");

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
                report.AppendLine(
                    $"LIGHTMASK: {lightmask.Width} x {lightmask.Height}");
                report.AppendLine(
                    $"ENVIRONMENT: {environment.Width} x {environment.Height}");

                report.AppendLine();
                report.AppendLine("MATERIAL COUNTS");

                for (int material = 0; material <= 9; material++)
                {
                    int count = model.Pieces.Count(
                        p => p.MaterialIndex == material);

                    report.AppendLine(
                        $"Material {material}: {count} pieces");
                }

                report.AppendLine();
                report.AppendLine("UV CHANNEL DIAGNOSTICS");

                for (int material = 0; material <= 9; material++)
                {
                    report.AppendLine();
                    report.AppendLine($"MATERIAL {material}");

                    foreach (var piece in model.Pieces.Where(
                        p => p.MaterialIndex == material))
                    {
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

                                if (!float.IsFinite(uv.X) ||
                                    !float.IsFinite(uv.Y))
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
                        report.AppendLine(
                            Encoding.UTF8.GetString(Extract(path)));
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine(
                            "MATERIAL READ ERROR: " + ex.Message);
                    }
                }

                report.AppendLine();
                report.AppendLine("FOUR TEXTURES DECODED SUCCESSFULLY.");
                report.AppendLine(
                    $"Original color.dds size: {colorBytes.Length} bytes");

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
            _status.Text =
                "BASE.SCS MODEL, TEXTURES AND MATERIALS LOADED";

            _renderButton.Enabled = true;
            _copyButton.Enabled = true;
            _exportButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _status.Text = "BASE.SCS LOADING FAILED\n" + ex.Message;
        }
        finally
        {
            SetBusy(false);
            _scanButton.Enabled = _defArchiveUri != null;
        }
    }

    private void SetBusy(bool busy)
    {
        _selectButton.Enabled = !busy;
        _selectDefButton.Enabled = !busy;
        _renderButton.Enabled = !busy && _model != null;
        _copyButton.Enabled =
            !busy && !string.IsNullOrWhiteSpace(_diagnosticReport);
        _exportButton.Enabled = !busy && _colorDds != null;
        _scanButton.Enabled = !busy && _defArchiveUri != null;
    }

    private async void ScanPaintAssets()
    {
        if (_defArchiveUri == null)
        {
            Toast.MakeText(
                this, "Select def.scs first.", ToastLength.Short)!.Show();
            return;
        }

        var uri = _defArchiveUri;
        SetBusy(true);

        _status.Text =
            "Scanning def.scs truck and paintjob definitions...";

        try
        {
            string scanReport = await Task.Run(() =>
            {
                using var input =
                    ContentResolver!.OpenInputStream(uri)
                    ?? throw new IOException("Cannot reopen def.scs.");

                using var reader = HashFsReader.Open(input);
                return BuildPaintAssetReport(reader);
            });

            _diagnosticReport += "\n\n" + scanReport;
            _report.Text = _diagnosticReport;
            _status.Text = "DEF.SCS PAINTJOB SCAN FINISHED";

            Toast.MakeText(
                this,
                "Paintjob scan completed. Copy the report.",
                ToastLength.Long)!.Show();
        }
        catch (Exception ex)
        {
            _status.Text = "DEF.SCS SCAN FAILED\n" + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string BuildPaintAssetReport(IHashFsReader reader)
    {
        var report = new StringBuilder();

        report.AppendLine("DAF XF PAINTJOB DEFINITION SCAN");
        report.AppendLine("===============================");
        report.AppendLine($"HashFS version: {reader.Version}");
        report.AppendLine(
            "Scanning truck definitions, paint_job folders and truck configurations.");

        var roots = new[]
        {
            "/def/vehicle/truck/",
            "/def/vehicle/truck_dealer/",
            "/def/vehicle/truck_dealer_uk/",
            "/def/vehicle/truck_desktop/",
            "/def/vehicle/truck_desktop_uk/",
            "/def/vehicle/truck_company/",
            "/def/vehicle/truck_company_uk/",
            "/def/vehicle/truck_used/",
            "/def/vehicle/truck_used_uk/"
        };

        var pending = new Stack<(string Path, int Depth)>();
        int rootsFound = 0;

        foreach (string root in roots)
        {
            if (reader.EntryExists(root) == EntryType.Directory)
            {
                pending.Push((root, 0));
                rootsFound++;
            }
            else
            {
                report.AppendLine("ROOT NOT FOUND: " + root);
            }
        }

        var visited = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var candidates = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        int directoriesScanned = 0;
        int directoryErrors = 0;

        const int maxDirectories = 6000;
        const int maxDepth = 14;
        const int maxTextFiles = 300;

        while (pending.Count > 0 &&
               directoriesScanned < maxDirectories)
        {
            var current = pending.Pop();
            string directory = current.Path.TrimEnd('/') + "/";

            if (!visited.Add(directory))
                continue;

            try
            {
                var listing = reader.GetDirectoryListing(
                    directory, false, true);

                directoriesScanned++;

                foreach (string file in listing.Files)
                {
                    string path = ResolveArchivePath(directory, file);
                    string lower = path.ToLowerInvariant();
                    string ext = Path.GetExtension(path).ToLowerInvariant();

                    bool wantedExt =
                        ext == ".sii" ||
                        ext == ".sui" ||
                        ext == ".mat" ||
                        ext == ".tobj" ||
                        ext == ".dds";

                    bool relevantPath =
                        lower.Contains("daf") ||
                        lower.Contains("paint_job") ||
                        lower.Contains("paintjob");

                    if (wantedExt && relevantPath)
                        candidates.Add(path);
                }

                if (current.Depth >= maxDepth)
                    continue;

                foreach (string child in listing.Subdirectories)
                {
                    string path = ResolveArchivePath(
                        directory, child).TrimEnd('/') + "/";

                    if (!visited.Contains(path))
                        pending.Push((path, current.Depth + 1));
                }
            }
            catch
            {
                directoryErrors++;
            }
        }

        report.AppendLine();
        report.AppendLine(
            $"Definition roots found: {rootsFound} / {roots.Length}");
        report.AppendLine(
            $"Directories scanned: {directoriesScanned}");
        report.AppendLine(
            $"Directory listing errors: {directoryErrors}");
        report.AppendLine(
            $"Candidate DAF/paintjob files: {candidates.Count}");

        report.AppendLine();
        report.AppendLine("CANDIDATE DEFINITION AND RESOURCE PATHS");
        report.AppendLine("---------------------------------------");

        var ordered = candidates
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string path in ordered)
            report.AppendLine(path);

        report.AppendLine();
        report.AppendLine("PAINTJOB / TEXTURE REFERENCES");
        report.AppendLine("-----------------------------");

        int inspected = 0;
        int matching = 0;

        string[] extensions = { ".sii", ".sui", ".mat" };

        string[] needles =
        {
            "paint",
            "texture",
            "mask",
            "alternate_uvset",
            "base_texture_override",
            "data_path",
            "daf",
            ".tobj",
            ".dds",
            "/vehicle/truck/upgrade/paintjob",
            "/material/ui/accessory"
        };

        foreach (string path in ordered)
        {
            
                    string ext = Path.GetExtension(path).ToLowerInvariant();

                    bool wantedExt =
                        ext == ".sii" ||
                        ext == ".sui" ||
                        ext == ".mat" ||
                        ext == ".tobj" ||
                        ext == ".dds";

                    bool relevantPath =
                        lower.Contains("daf") ||
                        lower.Contains("paint_job") ||
                        lower.Contains("paintjob");

                    if (wantedExt && relevantPath)
                        candidates.Add(path);
                }

                if (current.Depth >= maxDepth)
                    continue;

                foreach (string child in listing.Subdirectories)
                {
                    string path = ResolveArchivePath(
                        directory, child).TrimEnd('/') + "/";

                    if (!visited.Contains(path))
                        pending.Push((path, current.Depth + 1));
                }
            }
            catch
            {
                directoryErrors++;
            }
        }

        report.AppendLine();
        report.AppendLine($"Definition roots found: {rootsFound} / {roots.Length}");
        report.AppendLine($"Directories scanned: {directoriesScanned}");
        report.AppendLine($"Directory listing errors: {directoryErrors}");
        report.AppendLine($"Candidate DAF/paintjob files: {candidates.Count}");

        report.AppendLine();
        report.AppendLine("CANDIDATE DEFINITION AND RESOURCE PATHS");
        report.AppendLine("---------------------------------------");

        var ordered = candidates
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string path in ordered)
            report.AppendLine(path);

        report.AppendLine();
        report.AppendLine("PAINTJOB / TEXTURE REFERENCES");
        report.AppendLine("-----------------------------");

        int inspected = 0;
        int matching = 0;

        string[] extensions = { ".sii", ".sui", ".mat" };

        string[] needles =
        {
            "paint",
            "texture",
            "mask",
            "alternate_uvset",
            "base_texture_override",
            "data_path",
            "daf",
            ".tobj",
            ".dds",
            "/vehicle/truck/upgrade/paintjob",
            "/material/ui/accessory"
        };

        foreach (string path in ordered)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (!extensions.Contains(ext))
                continue;

            if (inspected >= maxTextFiles)
                break;

            inspected++;

            try
            {
                byte[][] files = reader.Extract(path);

                if (files.Length == 0)
                    continue;

                string content = Encoding.UTF8.GetString(files[0]);
                var lines = new List<string>();

                foreach (string line in content.Split('\n'))
                {
                    string lower = line.ToLowerInvariant();

                    if (!needles.Any(n => lower.Contains(n)))
                        continue;

                    string value = line.Trim();

                    if (value.Length > 260)
                        value = value.Substring(0, 260);

                    if (!string.IsNullOrWhiteSpace(value))
                        lines.Add(value);

                    if (lines.Count >= 24)
                        break;
                }

                if (lines.Count == 0)
                    continue;

                matching++;
                report.AppendLine();
                report.AppendLine("FILE: " + path);

                foreach (string line in lines)
                    report.AppendLine("  " + line);
            }
            catch (Exception ex)
            {
                report.AppendLine();
                report.AppendLine(
                    "READ ERROR: " + path + " : " + ex.Message);
            }
        }

        report.AppendLine();
        report.AppendLine($"Text files inspected: {inspected}");
        report.AppendLine($"Files with relevant references: {matching}");
        report.AppendLine("SCAN COMPLETE.");
        report.AppendLine(
            "NOTE: This scan reads definitions in def.scs. Referenced DDS/TOBJ files may be stored in another SCS archive.");

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
                this, "Load base.scs first.", ToastLength.Short)!.Show();
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

        var clipboard = (ClipboardManager)GetSystemService(
            ClipboardService)!;

        clipboard.PrimaryClip = ClipData.NewPlainText(
            "ETS2 DAF XF material and paintjob report",
            _diagnosticReport);

        Toast.MakeText(
            this, "Full report copied!", ToastLength.Short)!.Show();
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

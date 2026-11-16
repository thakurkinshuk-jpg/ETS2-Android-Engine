using Android.App;
using Android.Content;
using Android.Opengl;
using Android.OS;
using Android.Widget;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;

namespace ETS2AndroidEngine;

[Activity(Label = "ETS2 Android Engine", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    private const int PickArchiveRequest = 1001;

    private TextView _status = null!;
    private TextView _report = null!;
    private Button _selectButton = null!;
    private Button _renderButton = null!;
    private Button _copyButton = null!;

    private PmgV20Model? _model;
    private DecodedDdsTexture? _color;
    private DecodedDdsTexture? _glass;
    private DecodedDdsTexture? _lightmask;
    private DecodedDdsTexture? _environment;

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

        layout.AddView(_selectButton);
        layout.AddView(_renderButton);
        layout.AddView(_copyButton);
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

    protected override async void OnActivityResult(
        int requestCode,
        Result resultCode,
        Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode != PickArchiveRequest ||
            resultCode != Result.Ok ||
            data?.Data == null)
            return;

        var uri = data.Data;

        _selectButton.Enabled = false;
        _renderButton.Enabled = false;
        _copyButton.Enabled = false;

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

                var model = PmgV20Reader.Read(
                    Extract("/vehicle/truck/daf_xf/truck.pmg"));

                var color = DdsTextureDecoder.Decode(
                    Extract("/vehicle/truck/daf_xf/color.dds"));

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

                return (
                    Model: model,
                    Color: color,
                    Glass: glass,
                    Lightmask: lightmask,
                    Environment: environment,
                    Report: report.ToString()
                );
            });

            _model = loaded.Model;
            _color = loaded.Color;
            _glass = loaded.Glass;
            _lightmask = loaded.Lightmask;
            _environment = loaded.Environment;

            _diagnosticReport = loaded.Report;
            _report.Text = _diagnosticReport;

            _status.Text = "MODEL, TEXTURES AND MATERIALS ANALYZED";

            _renderButton.Enabled = true;
            _copyButton.Enabled = true;
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
            "ETS2 DAF XF full material report",
            _diagnosticReport);

        Toast.MakeText(
            this,
            "Full material report copied!",
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

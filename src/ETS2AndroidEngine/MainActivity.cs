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

[Activity(
    Label = "ETS2 Android Engine",
    MainLauncher = true,
    Exported = true)]
public class MainActivity : Activity
{
    private const int PickArchiveRequest = 1001;

    private TextView _status = null!;
    private TextView _report = null!;
    private Button _selectButton = null!;
    private Button _renderButton = null!;
    private Button _copyButton = null!;

    private PmgV20Model? _loadedModel;
    private DecodedDdsTexture? _colorTexture;
    private DecodedDdsTexture? _glassTexture;

    private GLSurfaceView? _glView;
    private string _diagnosticReport = "";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var layout = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        layout.SetPadding(20, 20, 20, 20);

        var title = new TextView(this)
        {
            Text = "ETS2 ANDROID ENGINE\n\nDAF XF TEXTURE RENDERER",
            TextSize = 21
        };

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
            Text = "COPY REPORT",
            Enabled = false
        };

        _status = new TextView(this)
        {
            Text = "Select your original ETS2 base.scs archive.",
            TextSize = 15
        };

        _report = new TextView(this)
        {
            Text = "Model and texture information will appear here.",
            TextSize = 12
        };

        var scroll = new ScrollView(this);
        scroll.AddView(_report);

        _selectButton.Click += (_, _) => SelectArchive();
        _renderButton.Click += (_, _) => ShowTruck();
        _copyButton.Click += (_, _) => CopyReport();

        layout.AddView(title);
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

        _status.Text = "Loading DAF XF model and textures...";
        _report.Text = "";

        try
        {
            var loaded = await Task.Run(() =>
            {
                using var input =
                    ContentResolver!.OpenInputStream(uri)
                    ?? throw new IOException(
                        "Cannot open the selected archive.");

                using var reader = HashFsReader.Open(input);

                // Load the DAF XF model.
                var modelFiles = reader.Extract(
                    "/vehicle/truck/daf_xf/truck.pmg");

                if (modelFiles.Length == 0)
                    throw new FileNotFoundException(
                        "truck.pmg was not found.");

                var model = PmgV20Reader.Read(modelFiles[0]);

                // Load the main truck texture.
                var colorFiles = reader.Extract(
                    "/vehicle/truck/daf_xf/color.dds");

                if (colorFiles.Length == 0)
                    throw new FileNotFoundException(
                        "color.dds was not found.");

                var colorTexture =
                    DdsTextureDecoder.Decode(colorFiles[0]);

                // Load the separate glass texture.
                var glassFiles = reader.Extract(
                    "/vehicle/truck/share/glass_ex.dds");

                if (glassFiles.Length == 0)
                    throw new FileNotFoundException(
                        "glass_ex.dds was not found.");

                var glassTexture =
                    DdsTextureDecoder.Decode(glassFiles[0]);

                // Build a concise diagnostic report.
                var report = new StringBuilder();

                report.AppendLine("ETS2 DAF XF MODEL REPORT");
                report.AppendLine("------------------------");
                report.AppendLine(
                    $"Model pieces: {model.PieceCount}");
                report.AppendLine(
                    $"Model parts: {model.PartCount}");
                report.AppendLine(
                    $"Model bones: {model.BoneCount}");

                report.AppendLine();
                report.AppendLine("COLOR TEXTURE");
                report.AppendLine(
                    $"Size: {colorTexture.Width} x {colorTexture.Height}");
                report.AppendLine(
                    $"RGBA bytes: {colorTexture.Rgba.Length}");

                report.AppendLine();
                report.AppendLine("GLASS TEXTURE");
                report.AppendLine(
                    $"Size: {glassTexture.Width} x {glassTexture.Height}");
                report.AppendLine(
                    $"RGBA bytes: {glassTexture.Rgba.Length}");

                report.AppendLine();
                report.AppendLine("MATERIAL SUMMARY");

                for (int i = 0; i < model.Pieces.Count; i++)
                {
                    var piece = model.Pieces[i];

                    report.AppendLine(
                        $"Piece {piece.Index}: " +
                        $"Vertices {piece.VertexCount}, " +
                        $"Triangles {piece.Indices.Length / 3}, " +
                        $"Material {piece.MaterialIndex}, " +
                        $"UV channels {piece.TexCoordWidth}");
                }

                report.AppendLine();
                report.AppendLine("TEXTURE LOADING COMPLETED.");

                return (
                    Model: model,
                    Color: colorTexture,
                    Glass: glassTexture,
                    Report: report.ToString()
                );
            });

            _loadedModel = loaded.Model;
            _colorTexture = loaded.Color;
            _glassTexture = loaded.Glass;

            _diagnosticReport = loaded.Report;
            _report.Text = _diagnosticReport;

            _status.Text =
                "MODEL AND BOTH TEXTURES LOADED SUCCESSFULLY";

            _renderButton.Enabled = true;
            _copyButton.Enabled = true;
            _selectButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _status.Text =
                "LOADING FAILED\n" + ex.Message;

            _selectButton.Enabled = true;
        }
    }

    private void ShowTruck()
    {
        if (_loadedModel == null ||
            _colorTexture == null ||
            _glassTexture == null)
        {
            Toast.MakeText(
                this,
                "Load the model and textures first.",
                ToastLength.Short)!.Show();

            return;
        }

        _glView = new GLSurfaceView(this);

        _glView.SetEGLContextClientVersion(2);

        // IMPORTANT:
        // Pass model, colour texture and glass texture.
        _glView.SetRenderer(
            new DafXfRenderer(
                _loadedModel,
                _colorTexture,
                _glassTexture));

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
            "ETS2 DAF XF report",
            _diagnosticReport);

        Toast.MakeText(
            this,
            "Report copied!",
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

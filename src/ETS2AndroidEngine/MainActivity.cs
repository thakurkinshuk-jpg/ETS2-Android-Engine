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
    private ScrollView _scroll = null!;

    private GLSurfaceView? _glView;
    private PmgV20Model? _loadedModel;
    private DecodedDdsTexture? _loadedTexture;
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
            Text = "ETS2 ANDROID ENGINE\n\nDAF XF MATERIAL DIAGNOSTICS",
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
            Text = "Diagnostic report will appear here.",
            TextSize = 12
        };

        _scroll = new ScrollView(this);
        _scroll.AddView(_report);

        _selectButton.Click += (_, _) => SelectArchive();
        _renderButton.Click += (_, _) => ShowTruck();
        _copyButton.Click += (_, _) => CopyReport();

        layout.AddView(title);
        layout.AddView(_selectButton);
        layout.AddView(_renderButton);
        layout.AddView(_copyButton);
        layout.AddView(_status);
        layout.AddView(_scroll);

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

        _diagnosticReport = "";
        _status.Text = "Inspecting DAF XF files...";
        _report.Text = "";

        try
        {
            var loaded = await Task.Run(() =>
            {
                using var input =
                    ContentResolver!.OpenInputStream(uri)
                    ?? throw new IOException("Cannot open archive.");

                using var reader = HashFsReader.Open(input);

                var pmdFiles = reader.Extract(
                    "/vehicle/truck/daf_xf/truck.pmd");

                var pmgFiles = reader.Extract(
                    "/vehicle/truck/daf_xf/truck.pmg");

                var ddsFiles = reader.Extract(
                    "/vehicle/truck/daf_xf/color.dds");

                if (pmdFiles.Length == 0)
                    throw new FileNotFoundException("truck.pmd not found.");

                if (pmgFiles.Length == 0)
                    throw new FileNotFoundException("truck.pmg not found.");

                if (ddsFiles.Length == 0)
                    throw new FileNotFoundException("color.dds not found.");

                var model = PmgV20Reader.Read(pmgFiles[0]);
                var texture = DdsTextureDecoder.Decode(ddsFiles[0]);

                string report = BuildReport(
                    reader.Version,
                    reader.Entries.Count,
                    pmdFiles[0],
                    model,
                    texture);

                return (
                    Model: model,
                    Texture: texture,
                    Report: report
                );
            });

            _loadedModel = loaded.Model;
            _loadedTexture = loaded.Texture;

            _diagnosticReport = loaded.Report;
            _report.Text = _diagnosticReport;

            _status.Text = "DIAGNOSTIC INSPECTION COMPLETED";

            _renderButton.Enabled = true;
            _copyButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _status.Text =
                "INSPECTION FAILED\n" +
                ex.GetType().Name + ": " + ex.Message;

            _selectButton.Enabled = true;
        }
    }

    private void CopyReport()
    {
        if (string.IsNullOrWhiteSpace(_diagnosticReport))
            return;

        var clipboard =
            (ClipboardManager)GetSystemService(ClipboardService)!;

        clipboard.PrimaryClip = ClipData.NewPlainText(
            "ETS2 DAF XF diagnostic report",
            _diagnosticReport);

        Toast.MakeText(
            this,
            "Diagnostic report copied!",
            ToastLength.Short)!.Show();
    }

    private static string BuildReport(
        ushort hashVersion,
        int entryCount,
        byte[] pmd,
        PmgV20Model model,
        DecodedDdsTexture texture)
    {
        var result = new StringBuilder();

        result.AppendLine("ETS2 DAF XF MATERIAL DIAGNOSTICS");
        result.AppendLine("--------------------------------");
        result.AppendLine($"HashFS version: {hashVersion}");
        result.AppendLine($"Archive entries: {entryCount}");

        result.AppendLine();
        result.AppendLine("TRUCK.PMD");
        result.AppendLine("---------");
        result.AppendLine($"File size: {pmd.Length} bytes");

        result.AppendLine();
        result.AppendLine("PMD HEX DATA (FIRST 256 BYTES)");

        int limit = Math.Min(pmd.Length, 256);

        for (int i = 0; i < limit; i += 16)
        {
            int count = Math.Min(16, limit - i);

            result.Append($"{i:X4}: ");

            for (int j = 0; j < count; j++)
                result.Append($"{pmd[i + j]:X2} ");

            result.AppendLine();
        }

        result.AppendLine();
        result.AppendLine("READABLE PMD STRINGS");
        result.AppendLine("--------------------");

        var current = new StringBuilder();

        foreach (byte value in pmd)
        {
            if (value >= 32 && value <= 126)
            {
                current.Append((char)value);
            }
            else
            {
                if (current.Length >= 4)
                    result.AppendLine(current.ToString());

                current.Clear();
            }
        }

        if (current.Length >= 4)
            result.AppendLine(current.ToString());

        result.AppendLine();
        result.AppendLine("PMG MODEL INFORMATION");
        result.AppendLine("---------------------");
        result.AppendLine($"Pieces: {model.PieceCount}");
        result.AppendLine($"Parts: {model.PartCount}");
        result.AppendLine($"Bones: {model.BoneCount}");

        foreach (var piece in model.Pieces)
        {
            result.AppendLine();
            result.AppendLine($"PIECE {piece.Index}");
            result.AppendLine($"Vertices: {piece.VertexCount}");
            result.AppendLine($"Triangles: {piece.Indices.Length / 3}");
            result.AppendLine($"Material index: {piece.MaterialIndex}");
            result.AppendLine($"UV channel count: {piece.TexCoordWidth}");
        }

        result.AppendLine();
        result.AppendLine("TEXTURE INFORMATION");
        result.AppendLine("-------------------");
        result.AppendLine($"Width: {texture.Width}");
        result.AppendLine($"Height: {texture.Height}");
        result.AppendLine($"RGBA bytes: {texture.Rgba.Length}");

        result.AppendLine();
        result.AppendLine("END OF DIAGNOSTIC REPORT");

        return result.ToString();
    }

    private void ShowTruck()
    {
        if (_loadedModel == null || _loadedTexture == null)
            return;

        _glView = new GLSurfaceView(this);
        _glView.SetEGLContextClientVersion(2);

        _glView.SetRenderer(
            new DafXfRenderer(_loadedModel, _loadedTexture));

        _glView.RenderMode = Rendermode.Continuously;

        SetContentView(_glView);
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

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
        "/automat/3b/3be06047c43a8f77.mat",
        "/automat/ec/ecc5854f240fb7d9.mat"
    };

    private TextView _status = null!;
    private TextView _report = null!;
    private Button _selectButton = null!;
    private Button _renderButton = null!;
    private Button _copyButton = null!;

    private PmgV20Model? _loadedModel;
    private DecodedDdsTexture? _loadedTexture;
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
            Text = "ETS2 ANDROID ENGINE\n\nDAF XF MATERIAL INSPECTION",
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
            Text = "Material diagnostic report will appear here.",
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

        _diagnosticReport = "";
        _report.Text = "";
        _status.Text = "Inspecting truck and material files...\nPlease wait.";

        try
        {
            var loaded = await Task.Run(() =>
            {
                string stage = "Opening selected archive";

                try
                {
                    using var input =
                        ContentResolver!.OpenInputStream(uri)
                        ?? throw new IOException("Cannot open archive.");

                    using var reader = HashFsReader.Open(input);

                    stage = "Extracting truck.pmd";

                    var pmdFiles = reader.Extract(
                        "/vehicle/truck/daf_xf/truck.pmd");

                    stage = "Extracting truck.pmg";

                    var pmgFiles = reader.Extract(
                        "/vehicle/truck/daf_xf/truck.pmg");

                    stage = "Extracting color.dds";

                    var ddsFiles = reader.Extract(
                        "/vehicle/truck/daf_xf/color.dds");

                    if (pmdFiles.Length == 0)
                        throw new FileNotFoundException("truck.pmd not found.");

                    if (pmgFiles.Length == 0)
                        throw new FileNotFoundException("truck.pmg not found.");

                    if (ddsFiles.Length == 0)
                        throw new FileNotFoundException("color.dds not found.");

                    stage = "Parsing DAF XF model";

                    var model = PmgV20Reader.Read(pmgFiles[0]);

                    stage = "Decoding DAF XF texture";

                    var texture = DdsTextureDecoder.Decode(ddsFiles[0]);

                    var materialData =
                        new byte[]?[MaterialPaths.Length];

                    for (int i = 0; i < MaterialPaths.Length; i++)
                    {
                        try
                        {
                            var files = reader.Extract(MaterialPaths[i]);

                            if (files.Length > 0)
                                materialData[i] = files[0];
                        }
                        catch (FileNotFoundException)
                        {
                            // Missing material files are optional.
                            // Continue inspecting the remaining files.
                            materialData[i] = null;
                        }
                    }

                    stage = "Building material diagnostic report";

                    string materialReport =
                        BuildMaterialReport(materialData);

                    string report = BuildReport(
                        reader.Version.ToString(),
                        reader.Entries.Count,
                        pmdFiles[0],
                        model,
                        texture,
                        materialReport);

                    return (
                        Model: model,
                        Texture: texture,
                        Report: report
                    );
                }
                catch (Exception ex)
                {
                    throw new IOException(
                        "Failed during: " + stage +
                        "\nOriginal error: " + ex.GetType().Name +
                        ": " + ex.Message,
                        ex);
                }
            });

            _loadedModel = loaded.Model;
            _loadedTexture = loaded.Texture;
            _diagnosticReport = loaded.Report;

            _report.Text = _diagnosticReport;
            _status.Text = "TRUCK AND MATERIAL INSPECTION COMPLETED";

            _renderButton.Enabled = true;
            _copyButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _status.Text =
                "INSPECTION FAILED\n" + ex.Message;

            _selectButton.Enabled = true;
        }
    }

    private static string BuildMaterialReport(byte[]?[] materialData)
    {
        var result = new StringBuilder();

        result.AppendLine();
        result.AppendLine("MATERIAL FILE INSPECTION");
        result.AppendLine("========================");

        for (int i = 0; i < MaterialPaths.Length; i++)
        {
            result.AppendLine();
            result.AppendLine("----------------------------------------");
            result.AppendLine("FILE: " + MaterialPaths[i]);
            result.AppendLine("----------------------------------------");

            byte[]? data = materialData[i];

            if (data == null)
            {
                result.AppendLine("STATUS: NOT FOUND");
                continue;
            }

            result.AppendLine("STATUS: FOUND");
            result.AppendLine($"SIZE: {data.Length} bytes");

            result.AppendLine();
            result.AppendLine("FIRST 96 BYTES:");

            int limit = Math.Min(data.Length, 96);

            for (int j = 0; j < limit; j += 16)
            {
                int count = Math.Min(16, limit - j);

                result.Append($"{j:X4}: ");

                for (int k = 0; k < count; k++)
                    result.Append($"{data[j + k]:X2} ");

                result.AppendLine();
            }

            result.AppendLine();
            result.AppendLine("READABLE CONTENT:");

            string decoded = Encoding.UTF8.GetString(data);

            var readable = new StringBuilder();

            foreach (char c in decoded)
            {
                if (c == '\r' || c == '\n' || c == '\t' ||
                    !char.IsControl(c))
                    readable.Append(c);
            }

            string content = readable.ToString().Trim();

            if (content.Length == 0)
            {
                result.AppendLine("(No readable text detected.)");
            }
            else
            {
                const int maxCharacters = 12000;

                if (content.Length > maxCharacters)
                {
                    result.AppendLine(
                        content.Substring(0, maxCharacters));

                    result.AppendLine("[CONTENT TRUNCATED]");
                }
                else
                {
                    result.AppendLine(content);
                }
            }
        }

        result.AppendLine();
        result.AppendLine("END OF MATERIAL INSPECTION");

        return result.ToString();
    }

    private static string BuildReport(
        string hashVersion,
        int entryCount,
        byte[] pmd,
        PmgV20Model model,
        DecodedDdsTexture texture,
        string materialReport)
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
        result.AppendLine("READABLE PMD STRINGS");
        result.AppendLine("--------------------");

        var current = new StringBuilder();

        foreach (byte value in pmd)
        {
            if (value >= 32 && value <= 126)
                current.Append((char)value);
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

        result.AppendLine(materialReport);
        result.AppendLine();
        result.AppendLine("END OF DIAGNOSTIC REPORT");

        return result.ToString();
    }

    private void CopyReport()
    {
        if (string.IsNullOrWhiteSpace(_diagnosticReport))
            return;

        var clipboard =
            (ClipboardManager)GetSystemService(ClipboardService)!;

        clipboard.PrimaryClip = ClipData.NewPlainText(
            "ETS2 material diagnostic report",
            _diagnosticReport);

        Toast.MakeText(
            this,
            "Diagnostic report copied!",
            ToastLength.Short)!.Show();
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

using Android.App;
using Android.Content;
using Android.Opengl;
using Android.OS;
using Android.Widget;
using System;
using System.IO;
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
    private Button _selectButton = null!;
    private GLSurfaceView? _glView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var layout = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        layout.SetPadding(24, 24, 24, 24);

        var title = new TextView(this)
        {
            Text = "ETS2 ANDROID ENGINE\n\nDAF XF TEXTURE TEST",
            TextSize = 22
        };

        _selectButton = new Button(this)
        {
            Text = "SELECT ETS2 BASE.SCS"
        };

        _status = new TextView(this)
        {
            Text = "Select your original ETS2 base.scs archive.",
            TextSize = 16
        };

        _selectButton.Click += (_, _) => SelectArchive();

        layout.AddView(title);
        layout.AddView(_selectButton);
        layout.AddView(_status);

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
        _status.Text = "Loading DAF XF model and texture...\nPlease wait.";

        try
        {
            var loaded = await Task.Run(() =>
            {
                using var input =
                    ContentResolver!.OpenInputStream(uri)
                    ?? throw new IOException("Cannot open selected archive.");

                using var reader = HashFsReader.Open(input);

                var modelFiles = reader.Extract(
                    "/vehicle/truck/daf_xf/truck.pmg");

                if (modelFiles.Length == 0)
                    throw new FileNotFoundException(
                        "DAF XF truck.pmg was not found.");

                var textureFiles = reader.Extract(
                    "/vehicle/truck/daf_xf/color.dds");

                if (textureFiles.Length == 0)
                    throw new FileNotFoundException(
                        "DAF XF color.dds was not found.");

                var model = PmgV20Reader.Read(modelFiles[0]);
                var texture = DdsTextureDecoder.Decode(textureFiles[0]);

                return (Model: model, Texture: texture);
            });

            _glView = new GLSurfaceView(this);
            _glView.SetEGLContextClientVersion(2);
            _glView.SetRenderer(
                new DafXfRenderer(loaded.Model, loaded.Texture));
            _glView.RenderMode = Rendermode.Continuously;

            SetContentView(_glView);
        }
        catch (Exception ex)
        {
            _status.Text =
                "MODEL OR TEXTURE LOADING FAILED\n\n" +
                ex.GetType().Name + "\n" +
                ex.Message;

            _selectButton.Enabled = true;
        }
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

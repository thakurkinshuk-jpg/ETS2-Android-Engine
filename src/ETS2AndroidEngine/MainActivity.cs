using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ETS2AndroidEngine;

[Activity(
    Label = "ETS2 Android Engine",
    MainLauncher = true,
    Exported = true)]
public class MainActivity : Activity
{
    private const int PickArchiveRequest = 1001;

    private TextView _status = null!;
    private TextView _output = null!;
    private Button _selectButton = null!;

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
            Text = "ETS2 ANDROID ENGINE\n\nHashFS Archive Reader",
            TextSize = 22
        };

        _selectButton = new Button(this)
        {
            Text = "SELECT ETS2 SCS ARCHIVE"
        };

        _status = new TextView(this)
        {
            Text = "No archive selected.",
            TextSize = 14
        };

        _output = new TextView(this)
        {
            Text = "Choose an original ETS2 .scs file to inspect it.",
            TextSize = 14
        };

        _output.SetTextIsSelectable(true);

        _selectButton.Click += (_, _) => SelectArchive();

        var scroll = new ScrollView(this);
        scroll.AddView(_output);

        layout.AddView(title);
        layout.AddView(_selectButton);
        layout.AddView(_status);
        layout.AddView(
            scroll,
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                0,
                1f));

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
        {
            return;
        }

        var uri = data.Data;

        _selectButton.Enabled = false;
        _status.Text = "Reading selected archive...";
        _output.Text = "Please wait. Large archives may take some time.";

        try
        {
            string report = await Task.Run(() =>
            {
                string tempPath = Path.Combine(
                    CacheDir!.AbsolutePath,
                    $"ets2-{Guid.NewGuid():N}.scs");

                try
                {
                    using (var input =
                        ContentResolver!.OpenInputStream(uri)
                        ?? throw new IOException("Cannot open selected file."))
                    using (var output = File.Create(tempPath))
                    {
                        input.CopyTo(output);
                    }

                    using var archive = File.OpenRead(tempPath);

                    return HashFsArchiveInspector.Inspect(archive);
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            });

            _output.Text = report;
            _status.Text = "Archive inspection completed.";
        }
        catch (Exception ex)
        {
            _status.Text = "Archive inspection failed.";
            _output.Text = ex.GetType().Name + "\n" + ex.Message;
        }
        finally
        {
            _selectButton.Enabled = true;
        }
    }
}


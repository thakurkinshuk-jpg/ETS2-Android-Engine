
using Android.App;
using Android.OS;
using Android.Widget;

namespace ETS2AndroidEngine;

[Activity(
    Label = "ETS2 Android Engine",
    MainLauncher = true,
    Exported = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var title = new TextView(this)
        {
            Text = "ETS2 Android Engine\n\nNative Android development started.",
            TextSize = 22
        };

        title.Gravity = Android.Views.GravityFlags.Center;
        SetContentView(title);
    }
}

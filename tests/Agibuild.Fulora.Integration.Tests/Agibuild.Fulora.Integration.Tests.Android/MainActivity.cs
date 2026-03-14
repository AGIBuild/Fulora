using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Agibuild.Fulora.Integration.Tests.Android;

[Activity(
    Label = "Agibuild.Fulora.Integration.Tests.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}

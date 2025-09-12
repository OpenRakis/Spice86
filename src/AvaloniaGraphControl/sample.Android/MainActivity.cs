using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Avalonia;
using Avalonia.Android;

namespace AvaloniaGraphControlSample.Android;

[Activity(
  Label = "AvaloniaGraphControlSample.Android",
  Theme = "@style/MyTheme.NoActionBar",
  Icon = "@drawable/icon",
  MainLauncher = true,
  ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
  protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
  {
    return base.CustomizeAppBuilder(builder)
      .WithInterFont()
      .SetUrlOpener(OpenUrl);
  }
  
  private void OpenUrl(string url)
  {
    var uri = Uri.Parse (url);
    var intent = new Intent (Intent.ActionView, uri);
    StartActivity(intent);
  }
}

using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using AvaloniaGraphControlSample;

[assembly: SupportedOSPlatform("browser")]

internal partial class Program
{
  private static async Task Main(string[] args) => await BuildAvaloniaApp()
    .WithInterFont()
    .StartBrowserAppAsync("out");

  public static AppBuilder BuildAvaloniaApp() => AppBuilder
      .Configure<App>()
      .SetUrlOpener(Interop.OpenUrl);
}

[SupportedOSPlatform("browser")]
internal static partial class Interop
{
  [JSImport("globalThis.open")]
  public static partial void OpenUrl(string url);
}

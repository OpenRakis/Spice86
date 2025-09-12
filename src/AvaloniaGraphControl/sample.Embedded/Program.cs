using Avalonia;
using Avalonia.Media;

namespace AvaloniaGraphControlSample.Embedded;

class Program
{
  [STAThread]
  public static void Main(string[] args)
  {
    BuildAvaloniaApp().StartLinuxFbDev(args);
  }
  
  public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
      .WithInterFont()
      .With(new FontManagerOptions
      {
        DefaultFamilyName = "avares://Avalonia.Fonts.Inter/Assets#Inter"
      })
      .LogToTrace()
      .SetUrlOpener(OpenUrl);
  
  private static void OpenUrl(string url)
  {
    Console.WriteLine($"Open {url}");
  }
}

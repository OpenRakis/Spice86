using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AvaloniaGraphControlSample
{
  public class App : Application
  {
    private Action<string> _openUrl = _ => {};
    public void SetUrlOpener(Action<string> openUrl)
    {
      _openUrl = openUrl;
    }
    
    public override void Initialize()
    {
      AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
      Trace.Listeners.Add(new ConsoleTraceListener());
      if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
      {
        desktop.MainWindow = new MainWindow()
        {
          DataContext = new Model(_openUrl)
        };
      }
      else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
      {
        singleViewPlatform.MainView = new MainView
        {
          DataContext = new Model(_openUrl)
        };
      }
      
      base.OnFrameworkInitializationCompleted();
    }
  }

  public static class ConfigureApp
  {
    public static AppBuilder SetUrlOpener(this AppBuilder builder, Action<string> openUrl) =>
      builder.AfterSetup(b => ((App)b.Instance!).SetUrlOpener(openUrl));
  }
}

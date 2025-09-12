using Avalonia.Media;
using AvaloniaGraphControl;

namespace AvaloniaGraphControlSample
{
  class StateMachine : NamedGraph
  {
    public StateMachine(Orientations orientation) : base($"State Machine ({orientation})")
    {
      Orientation = orientation;
      var initMain = new InitialState();
      var on = new CompositeState("On", Brushes.LightSkyBlue);
      var off = new State("Off", Brushes.CornflowerBlue);
      var cleaning = new State("Cleaning", Brushes.CornflowerBlue);
      var initOn = new InitialState();
      var available = new CompositeState("Available", Brushes.LightGreen);
      var failure = new State("Failure", Brushes.LimeGreen);
      var initAvailable = new InitialState();
      var paused = new State("Paused", Brushes.Yellow);
      var starting = new State("Starting", Brushes.Yellow);
      var started = new State("Started", Brushes.Yellow);
      var pausing = new State("Pausing", Brushes.Yellow);
      static Edge CreateEdge(object x, object y, object label = null) => new Edge(x, y, label);
      Edges.Add(CreateEdge(initMain, off));
      Edges.Add(CreateEdge(off, on, "SwitchOn"));
      Edges.Add(CreateEdge(on, cleaning, "SwitchOff"));
      Edges.Add(CreateEdge(cleaning, off, "CleaningComplete"));
      Edges.Add(CreateEdge(initOn, available));
      Edges.Add(CreateEdge(available, failure, "FailureDetected"));
      Edges.Add(CreateEdge(failure, available, "Reset"));
      Edges.Add(CreateEdge(initAvailable, paused));
      Edges.Add(CreateEdge(paused, starting, "Start"));
      Edges.Add(CreateEdge(starting, started, "StartingComplete"));
      Edges.Add(CreateEdge(started, pausing, "Pause"));
      Edges.Add(CreateEdge(pausing, paused, "PausingComplete"));
      Parent[initOn] = on;
      Parent[available] = on;
      Parent[failure] = on;
      Parent[initAvailable] = available;
      Parent[paused] = available;
      Parent[starting] = available;
      Parent[started] = available;
      Parent[pausing] = available;
      int Order(object s1, object s2)
      {
        if (s1 == s2 || Parent[s1] != Parent[s2])
          return 0;
        if (s1 is InitialState)
          return -1;
        if (s2 is InitialState)
          return 1;
        return 0;
      }
      if (orientation == Orientations.Vertical)
        VerticalOrder = Order;
      else
        HorizontalOrder = Order;
    }
  }
}

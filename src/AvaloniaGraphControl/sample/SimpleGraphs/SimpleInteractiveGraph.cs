using AvaloniaGraphControl;

namespace AvaloniaGraphControlSample
{
  class SimpleInteractiveGraph : NamedGraph
  {
    public SimpleInteractiveGraph() : base("Simple Interactive Graph")
    {
      var a = new InteractiveItem("A");
      var b = new InteractiveItem("B");
      var c = new InteractiveItem("C");
      var d = new InteractiveItem("D");
      var e = new InteractiveItem("E");
      Edges.Add(new Edge(a, b));
      Edges.Add(new Edge(a, d));
      Edges.Add(new Edge(a, e));
      Edges.Add(new Edge(b, c));
      Edges.Add(new Edge(b, d));
      Edges.Add(new Edge(d, a));
      Edges.Add(new Edge(d, e));
    }
  }
}

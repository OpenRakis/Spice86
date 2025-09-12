using AvaloniaGraphControl;

namespace AvaloniaGraphControlSample
{
  class SimpleGraph : NamedGraph
  {
    public SimpleGraph() : base("Simple Graph")
    {
      var a = new StandardItem("A");
      var b = new StandardItem("B");
      var c = new StandardItem("C");
      var d = new StandardItem("D");
      var e = new StandardItem("E");
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

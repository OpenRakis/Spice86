using AvaloniaGraphControl;

namespace AvaloniaGraphControlSample
{
  class SimpleWithSubgraph : NamedGraph
  {
    public SimpleWithSubgraph() : base("Simple Graph (with subgraph)")
    {
      var a = new StandardItem("A");
      var b = new CompositeItem("B");
      var b1 = new StandardItem("B1");
      var b2 = new StandardItem("B2");
      var b3 = new StandardItem("B3");
      var b4 = new StandardItem("B4");
      var c = new StandardItem("C");
      var d = new StandardItem("D");
      Edges.Add(new Edge(a, b));
      Edges.Add(new Edge(a, c));
      Edges.Add(new Edge(b, d));
      Edges.Add(new Edge(c, d));
      Edges.Add(new Edge(b1, b2));
      Edges.Add(new Edge(b1, b3));
      Edges.Add(new Edge(b2, b4));
      Edges.Add(new Edge(b3, b4));
      Parent[b1] = b;
      Parent[b2] = b;
      Parent[b3] = b;
      Parent[b4] = b;
    }
  }
}

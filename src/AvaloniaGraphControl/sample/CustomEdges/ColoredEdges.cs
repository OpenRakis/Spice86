using AvaloniaGraphControl;

namespace AvaloniaGraphControlSample
{
  class ColoredEdges : NamedGraph
  {
    public ColoredEdges() : base("Colored Edges")
    {
      var a = new StandardItem("A");
      var b = new StandardItem("B");
      var c = new StandardItem("C");
      var d = new StandardItem("D");
      var e = new StandardItem("E");
      Edges.Add(new ColoredEdge(a, b));
      Edges.Add(new ColoredEdge(a, d));
      Edges.Add(new ColoredEdge(a, e));
      Edges.Add(new ColoredEdge(b, c));
      Edges.Add(new ColoredEdge(b, d));
      Edges.Add(new ColoredEdge(d, a));
      Edges.Add(new ColoredEdge(d, e));
    }
  }
  class ColoredEdge : Edge
  {
    public ColoredEdge(StandardItem tail, StandardItem head) : base(tail, head)
    {
      if (tail.Name == "A")
        MyCustomColor = (head.Name == "B") ? "Plum" : "Peru";
      else if (tail.Name == "B")
        MyCustomColor = "DarkRed";
      else
        MyCustomColor = "YellowGreen";
    }
    public string MyCustomColor { get; }
  }
}


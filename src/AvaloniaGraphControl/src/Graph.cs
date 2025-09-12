
using System;
using System.Collections.Generic;

namespace AvaloniaGraphControl
{
  public class Graph
  {
    public Graph()
    {
      Edges = new List<Edge>();
      Parent = new Indexer<object, object>(k => hierarchy.GetValueOrDefault(k), (k, v) => hierarchy[k] = v);
      Orientation = Orientations.Vertical;
      HorizontalOrder = (x1, x2) => 0;
      VerticalOrder = (x1, x2) => 0;
    }
    public readonly ICollection<Edge> Edges;
    public readonly Indexer<object, object> Parent;
    public enum Orientations
    {
      Vertical,
      Horizontal
    }
    public Orientations Orientation { get; set; }
    public Func<object, object, int> HorizontalOrder { get; set; }
    public Func<object, object, int> VerticalOrder { get; set; }

    private readonly Dictionary<object, object> hierarchy = new Dictionary<object, object>();
  }
}
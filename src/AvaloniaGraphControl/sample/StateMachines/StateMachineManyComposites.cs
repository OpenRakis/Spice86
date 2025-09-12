using Avalonia.Media;
using AvaloniaGraphControl;

namespace AvaloniaGraphControlSample
{
  class StateMachineManyComposites : NamedGraph
  {
    public StateMachineManyComposites(Orientations orientation) : base($"State Machine Many Composites ({orientation})")
    {
       Orientation = orientation;
      var initMain = new InitialState();
      var a = new State("A", Brushes.CornflowerBlue);
      var b = new CompositeState("B", Brushes.LightSkyBlue);
      var initB = new InitialState();
      var ba = new CompositeState("BA", Brushes.LightGreen);
      var initBa = new InitialState();
      var baa = new State("BAA", Brushes.Yellow);
      var bab = new State("BAB", Brushes.Yellow);
      var bac = new State("BAC", Brushes.Yellow);
      var bad = new State("BAD", Brushes.Yellow);
      
      var bb = new CompositeState("BB", Brushes.LimeGreen);
      var initBb = new InitialState();
      var bba = new State("BBA", Brushes.Yellow);
      var bbb = new State("BBB", Brushes.Yellow);
      var bbc = new State("BBC", Brushes.Yellow);
      var bbd = new State("BBD", Brushes.Yellow);
      var c = new State("C", Brushes.CornflowerBlue);
      static Edge CreateEdge(object x, object y, object label = null) => new Edge(x, y, label);
      Edges.Add(CreateEdge(initMain, a));
      Edges.Add(CreateEdge(a, b, "aTob"));
      Edges.Add(CreateEdge(b, c, "bToc"));
      Edges.Add(CreateEdge(c, a, "cToA"));
      
      Edges.Add(CreateEdge(initB, ba));
      Edges.Add(CreateEdge(ba, bb, "baTobb"));
      Edges.Add(CreateEdge(bb, ba, "bbToba"));
      
      Edges.Add(CreateEdge(initBa, baa));
      Edges.Add(CreateEdge(baa, bab, "baaTobab"));
      Edges.Add(CreateEdge(bab, bac, "babTobac"));
      Edges.Add(CreateEdge(bac, bad, "bacTobad"));
      Edges.Add(CreateEdge(bad, baa, "badTobaa"));
      
      Edges.Add(CreateEdge(initBb, bba));
      Edges.Add(CreateEdge(bba, bbb, "bbaTobbb"));
      Edges.Add(CreateEdge(bbb, bbc, "bbbTobbc"));
      Edges.Add(CreateEdge(bbc, bbd, "bbcTobdd"));
      
      Parent[initB] = b;
      Parent[ba] = b;
      Parent[initBa] = ba;
      Parent[baa] = ba;
      Parent[bab] = ba;
      Parent[bac] = ba;
      Parent[bad] = ba;
      
      Parent[ba] = b;
      Parent[initBa] = ba;
      Parent[baa] = ba;
      Parent[bab] = ba;
      Parent[bac] = ba;
      Parent[bad] = ba;
      
      Parent[bb] = b;
      Parent[initBb] = bb;
      Parent[bba] = bb;
      Parent[bbb] = bb;
      Parent[bbc] = bb;
      Parent[bbd] = bb;
      
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

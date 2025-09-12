using System;
using System.Linq;
using Avalonia.Media;
using AvaloniaGraphControl;

namespace AvaloniaGraphControlSample
{
  class FamilyMember
  {
    private readonly Action onClick;

    public FamilyMember(string name, IBrush backgroungColor, Action onClick)
    {
      this.onClick = onClick;
      Name = name;
      BackgroundColor = backgroungColor;
    }

    public void Navigate() => onClick();
    public string Name { get; private set; }
    public IBrush BackgroundColor { get; private set; }
  }

  class Male : FamilyMember
  {
    public Male(string name, Action onClick) : base(name, Brushes.LightSkyBlue, onClick) { }
  }
  class Female : FamilyMember
  {
    public Female(string name, Action onClick) : base(name, Brushes.LightPink, onClick) { }
  }
  class Family
  {
  }

  class FamilyTree : NamedGraph
  {
    public FamilyTree(Action<string> openUrl) : base("Family Tree")
    {
      Orientation = Orientations.Vertical;
      var abraham = new Male("Abraham", () => openUrl("https://en.wikipedia.org/wiki/Grampa_Simpson"));
      var mona = new Female("Mona", () => openUrl("https://en.wikipedia.org/wiki/Mona_Simpson_(The_Simpsons)"));
      var homer = new Male("Homer", () => openUrl("https://en.wikipedia.org/wiki/Homer_Simpson"));
      var clancy = new Male("Clancy", () => openUrl("https://en.wikipedia.org/wiki/Simpson_family#Clancy_Bouvier"));
      var jackie = new Female("Jackie", () => openUrl("https://en.wikipedia.org/wiki/Simpson_family#Jacqueline_Bouvier"));
      var marge = new Female("Marge", () => openUrl("https://en.wikipedia.org/wiki/Marge_Simpson"));
      var patty = new Female("Patty", () => openUrl("https://en.wikipedia.org/wiki/Patty_and_Selma"));
      var selma = new Female("Selma", () => openUrl("https://en.wikipedia.org/wiki/Patty_and_Selma"));
      var ling = new Female("Ling", () => openUrl("https://en.wikipedia.org/wiki/Simpson_family#Ling_Bouvier"));
      var bart = new Male("Bart", () => openUrl("https://en.wikipedia.org/wiki/Bart_Simpson"));
      var lisa = new Female("Lisa", () => openUrl("https://en.wikipedia.org/wiki/Lisa_Simpson"));
      var maggie = new Female("Maggie", () => openUrl("https://en.wikipedia.org/wiki/Maggie_Simpson"));
      var f1 = new Family();
      var f2 = new Family();
      var f3 = new Family();
      static Edge CreateEdge(object x, object y) => new Edge(
        x, y, tailSymbol: Edge.Symbol.None, headSymbol: Edge.Symbol.None
      );
      Edges.Add(CreateEdge(abraham, f1));
      Edges.Add(CreateEdge(mona, f1));
      Edges.Add(CreateEdge(f1, homer));
      Edges.Add(CreateEdge(clancy, f2));
      Edges.Add(CreateEdge(jackie, f2));
      Edges.Add(CreateEdge(f2, marge));
      Edges.Add(CreateEdge(f2, patty));
      Edges.Add(CreateEdge(f2, selma));
      Edges.Add(CreateEdge(homer, f3));
      Edges.Add(CreateEdge(marge, f3));
      Edges.Add(CreateEdge(f3, bart));
      Edges.Add(CreateEdge(f3, lisa));
      Edges.Add(CreateEdge(f3, maggie));
      Edges.Add(CreateEdge(selma, ling));
      HorizontalOrder = (p1, p2) => !ParentsOfSameFamily(p1, p2) ? 0 : (p1 is Male) ? -1 : 1;
    }

    private bool ParentsOfSameFamily(object p1, object p2)
    {
      var fp1 = Edges.FirstOrDefault(e => e.Tail == p1)?.Head;
      var fp2 = Edges.FirstOrDefault(e => e.Tail == p2)?.Head;
      return fp1 != null && fp2 != null && fp1 == fp2;
    }
  }
}

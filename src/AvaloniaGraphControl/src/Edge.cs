
namespace AvaloniaGraphControl
{
  public class Edge
  {
    public Edge(object tail, object head, object label = null, Symbol tailSymbol = Symbol.None, Symbol headSymbol = Symbol.Arrow)
    {
      Tail = tail;
      Head = head;
      Label = label ?? string.Empty;
      TailSymbol = tailSymbol;
      HeadSymbol = headSymbol;
    }

    public enum Symbol
    {
      None = 0,
      Arrow
    }

    public readonly object Tail;
    public readonly object Head;
    public readonly object Label;
    public readonly Symbol TailSymbol;
    public readonly Symbol HeadSymbol;

    internal Microsoft.Msagl.Drawing.Edge DEdge { get; set; }

    internal static Microsoft.Msagl.Drawing.ArrowStyle GetArrowStyle(Symbol symbol) => symbol switch
    {
      Symbol.Arrow => Microsoft.Msagl.Drawing.ArrowStyle.Normal,
      _ => Microsoft.Msagl.Drawing.ArrowStyle.None
    };
  }
}
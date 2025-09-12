using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaGraphControl;

namespace AvaloniaGraphControlSample
{
  class NamedGraph : Graph
  {
    public NamedGraph(string name) { Name = name; }
    public string Name { get; private set; }
    public override string ToString() => Name;
  }

  class Model
  {
    private readonly Action<string> openUrl;

    public Model(Action<string> openUrl)
    {
      this.openUrl = openUrl;
    }

    public void OpenUrl(string url) => openUrl(url);
    
    public IEnumerable<NamedGraph> SampleGraphs => new NamedGraph[] {
      new SimpleGraph(),
      new SimpleOrderedLayoutGraph(),
      new SimpleInteractiveGraph(),
      new SimpleWithSubgraph(),
      new SimpleOrderedLayoutWithSubgraph(),
      new ColoredEdges(),
      new FamilyTree(openUrl),
      new StateMachine(Graph.Orientations.Vertical),
      new StateMachine(Graph.Orientations.Horizontal),
      new StateMachineManyComposites(Graph.Orientations.Vertical),
      new StateMachineManyComposites(Graph.Orientations.Horizontal),
    };

    public IEnumerable<GraphPanel.LayoutMethods> LayoutMethods => Enum.GetValues(typeof(GraphPanel.LayoutMethods)).Cast<GraphPanel.LayoutMethods>();

    public string Version => typeof(Graph).Assembly.GetName().Version?.ToString() ?? "Unknown";
  }
}

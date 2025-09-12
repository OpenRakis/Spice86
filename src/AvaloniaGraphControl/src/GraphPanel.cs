using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace AvaloniaGraphControl
{
  public class GraphPanel : Panel
  {
    public static readonly StyledProperty<Graph> GraphProperty = AvaloniaProperty.Register<GraphPanel, Graph>(nameof(Graph));

    public Graph Graph
    {
      get { return GetValue(GraphProperty); }
      set { SetValue(GraphProperty, value); }
    }

    public enum LayoutMethods
    {
      SugiyamaScheme,
      MDS,
      Ranking,
      IncrementalLayout
    }

    public static readonly StyledProperty<LayoutMethods> LayoutMethodProperty = AvaloniaProperty.Register<GraphPanel, LayoutMethods>(nameof(LayoutMethod), LayoutMethods.SugiyamaScheme);

    public LayoutMethods LayoutMethod
    {
      get { return GetValue(LayoutMethodProperty); }
      set { SetValue(LayoutMethodProperty, value); }
    }

    static GraphPanel()
    {
      GraphProperty.Changed.AddClassHandler<GraphPanel>((gp, _) => gp.CreateMSAGLGraphAndPopulatePanelWithAssociatedControls());
      LayoutMethodProperty.Changed.AddClassHandler<GraphPanel>((gp, _) => gp.CreateMSAGLGraphAndPopulatePanelWithAssociatedControls());
      AffectsMeasure<GraphPanel>(GraphProperty, LayoutMethodProperty);
    }

    public GraphPanel()
    {
      RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
    }

    private void CreateMSAGLGraphAndPopulatePanelWithAssociatedControls()
    {
      if (Graph == null)
        return;
      Children.Clear();
      idGenerator = new ObjectIDGenerator();
      var edgeVMs = Graph.Edges.ToArray();
      var nodeVMs = edgeVMs.Select(e => e.Head).Concat(edgeVMs.Select(e => e.Tail)).Distinct().Select(nvm => new NodeWrapper(nvm, idGenerator)).ToDictionary(nw => nw.VM, nw => nw);
      var parentVMs = nodeVMs.Select(kv => Graph.Parent[kv.Key]).Where(pvm => pvm != null).Distinct().Select(pvm => nodeVMs[pvm]).ToArray();
      var leafVMs = nodeVMs.Values.Except(parentVMs).ToArray();
      graph = new Microsoft.Msagl.Drawing.Graph
      {
        LayoutAlgorithmSettings = CurrentLayoutSettings
      };
      graph.RootSubgraph.IsVisible = false;
      vmOfCtrl = new Dictionary<Control, Wrapper>();
      foreach (var sgvm in parentVMs)
      {
        var sg = new Microsoft.Msagl.Drawing.Subgraph(sgvm.ID);
        sgvm.DNode = sg;
        var ctrl = CreateControl(sgvm.VM, n => new TextSticker
        {
          Text = n.ToString(),
          HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
          VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        }, 1);
        vmOfCtrl[ctrl] = sgvm;
      }
      foreach (var sgvm in parentVMs)
      {
        var parent = Graph.Parent[sgvm.VM];
        var pGraph = (parent == null) ? graph.RootSubgraph : (Microsoft.Msagl.Drawing.Subgraph)nodeVMs[parent].DNode;
        pGraph.AddSubgraph((Microsoft.Msagl.Drawing.Subgraph)sgvm.DNode);
      }
      foreach (var evm in edgeVMs)
      {
        var dEdge = graph.AddEdge(nodeVMs[evm.Tail].ID, nodeVMs[evm.Head].ID);
        dEdge.Attr.ArrowheadAtSource = Edge.GetArrowStyle(evm.TailSymbol);
        dEdge.Attr.ArrowheadAtTarget = Edge.GetArrowStyle(evm.HeadSymbol);
        dEdge.LabelText = "x";
        evm.DEdge = dEdge;
        CreateControl(evm, _ => new Connection(), 2);
      }
      foreach (var nvm in leafVMs)
      {
        var dNode = graph.FindNode(nvm.ID);
        nvm.DNode = dNode;
        var ctrl = CreateControl(nvm.VM, n => new TextSticker { Text = n.ToString() }, 4);
        vmOfCtrl[ctrl] = nvm;
        var parent = Graph.Parent[nvm.VM];
        if (parent != null)
        {
          var pw = nodeVMs[parent];
          ((Microsoft.Msagl.Drawing.Subgraph)pw.DNode).AddNode(dNode);
        }
      }

      graph.Attr.LayerDirection = (Graph.Orientation == Graph.Orientations.Vertical)
        ? Microsoft.Msagl.Drawing.LayerDirection.TB
        : Microsoft.Msagl.Drawing.LayerDirection.LR;
      var flowOrder = (Graph.Orientation == Graph.Orientations.Vertical) ? Graph.VerticalOrder : Graph.HorizontalOrder;
      var otherOrder = (Graph.Orientation == Graph.Orientations.Vertical) ? Graph.HorizontalOrder : Graph.VerticalOrder;

      IEnumerable<(NodeWrapper, NodeWrapper)> ComputeConstraints(Func<object, object, int> comparison) =>
        (from n1 in nodeVMs
         from n2 in nodeVMs
         let order = comparison(n1.Key, n2.Key)
         where order != 0
         select (order < 0) ? (n1.Value, n2.Value) : (n2.Value, n1.Value)).Distinct();
      foreach (var flowConstraint in ComputeConstraints(flowOrder))
        graph.LayerConstraints.AddUpDownConstraint(flowConstraint.Item1.DNode, flowConstraint.Item2.DNode);
      foreach (var otherConstraint in ComputeConstraints(otherOrder))
        graph.LayerConstraints.AddLeftRightConstraint(otherConstraint.Item1.DNode, otherConstraint.Item2.DNode);


      graph.CreateGeometryGraph();
      graph.GeometryGraph.RootCluster.RectangularBoundary = new Microsoft.Msagl.Core.Geometry.RectangularClusterBoundary();



      foreach (var evm in edgeVMs)
      {
        if (!evm.Label.Equals(string.Empty))
        {
          var ctrl = CreateControl(evm.Label, l => new TextBlock { Text = l.ToString(), FontSize = 6 }, 3);
          vmOfCtrl[ctrl] = new LabelWrapper(evm.Label, idGenerator, evm.DEdge.Label);
        }
      }
    }

    private Control CreateControl(object vm, Func<object, Control> getDefault, int zIndex)
    {
      var tpl = this.FindDataTemplate(vm);
      var ctrl = tpl == null ? getDefault(vm) : tpl.Build(vm);
      ctrl.DataContext = vm;
      Children.Add(ctrl);
      ctrl.ZIndex = zIndex;
      return ctrl;
    }

    Microsoft.Msagl.Drawing.Graph graph;
    ObjectIDGenerator idGenerator;
    Dictionary<Control, Wrapper> vmOfCtrl;

    abstract class Wrapper
    {
      public Wrapper(object vm, ObjectIDGenerator idGen)
      {
        VM = vm;
        ID = idGen.GetId(vm, out bool _).ToString();
      }
      public readonly object VM;
      public readonly string ID;

      internal abstract Microsoft.Msagl.Core.Geometry.Rectangle GetBoundingBox();
      internal abstract void UpdateBounds(Control ctrl);
    }

    class LabelWrapper : Wrapper
    {
      public LabelWrapper(object label, ObjectIDGenerator idGen, Microsoft.Msagl.Drawing.Label dLabel) : base(label, idGen)
      {
        DLabel = dLabel;
      }

      public readonly Microsoft.Msagl.Drawing.Label DLabel;
      internal override Microsoft.Msagl.Core.Geometry.Rectangle GetBoundingBox() => DLabel.BoundingBox;
      internal override void UpdateBounds(Control ctrl)
      {
        DLabel.Width = ctrl.DesiredSize.Width;
        DLabel.Height = ctrl.DesiredSize.Height;
      }
    }

    class NodeWrapper : Wrapper
    {
      public NodeWrapper(object node, ObjectIDGenerator idGen) : base(node, idGen) { }

      public Microsoft.Msagl.Drawing.Node DNode { get; set; }

      internal override Microsoft.Msagl.Core.Geometry.Rectangle GetBoundingBox() => DNode.BoundingBox;
      internal override void UpdateBounds(Control ctrl)
      {
        if (DNode.GeometryNode == null)
          return;
        var (shape, borderRadius) = ctrl is TextSticker ts ? (ts.Shape, ts.BorderRadius) : (TextSticker.Shapes.Rectangle, 0);
        DNode.GeometryNode.BoundaryCurve = AglCurveFactory.Create(shape, ctrl.DesiredSize, borderRadius);
      }
    }

    protected override Size MeasureOverride(Size constraint)
    {
      if (Graph == null)
        return new Size(0, 0);
      foreach (var child in Children)
      {
        child.Measure(constraint);
        if (vmOfCtrl.TryGetValue(child, out Wrapper w))
          w.UpdateBounds(child);
      }
      try
      {
        Microsoft.Msagl.Miscellaneous.LayoutHelpers.CalculateLayout(graph.GeometryGraph, graph.LayoutAlgorithmSettings,
          null);
      }
      catch (Exception e)
      {
        Trace.TraceError("Msagl layout error {0}", e);
      }
      var graphDesiredSize = AglToAvalonia.Convert(graph.BoundingBox.Size);
      return graphDesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
      if (Graph == null)
        return finalSize;
      var a2a = new AglToAvalonia(graph.BoundingBox.LeftTop);
      foreach (var child in Children)
      {
        var bbox = GetBoundingBox(child);
        if (!bbox.HasValue)
          continue;
        var childFinalSize = a2a.Convert(bbox.Value);
        child.Arrange(childFinalSize);
      }
      var graphSize = AglToAvalonia.Convert(graph.BoundingBox.Size);
      return graphSize;
    }

    private Microsoft.Msagl.Core.Geometry.Rectangle? GetBoundingBox(Control ctrl)
    {
      try
      {
        if (vmOfCtrl.TryGetValue(ctrl, out Wrapper w))
          return w.GetBoundingBox();
        if (ctrl is Connection c)
          return ((Edge)c.DataContext).DEdge.BoundingBox;
      }
      catch (Exception e)
      {
        Trace.TraceError("Msagl bounding box error {0}", e);
      }
      return null;
    }

    private Microsoft.Msagl.Core.Layout.LayoutAlgorithmSettings CurrentLayoutSettings =>
       LayoutMethod switch
       {
         LayoutMethods.SugiyamaScheme => new Microsoft.Msagl.Layout.Layered.SugiyamaLayoutSettings(),
         LayoutMethods.MDS => new Microsoft.Msagl.Layout.MDS.MdsLayoutSettings(),
         LayoutMethods.Ranking => new Microsoft.Msagl.Prototype.Ranking.RankingLayoutSettings(),
         LayoutMethods.IncrementalLayout => new Microsoft.Msagl.Layout.Incremental.FastIncrementalLayoutSettings(),
         _ => new Microsoft.Msagl.Layout.Layered.SugiyamaLayoutSettings()
       };
  }
}

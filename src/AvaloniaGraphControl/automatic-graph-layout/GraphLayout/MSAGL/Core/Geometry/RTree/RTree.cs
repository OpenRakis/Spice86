using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Msagl.Core.Layout;

namespace Microsoft.Msagl.Core.Geometry {
    /// <summary>
    /// A search tree for rapid lookup of TData objects keyed by rectangles inside a given rectangular region
    /// It is very similar to "R-TREES. A DYNAMIC INDEX STRUCTURE FOR SPATIAL SEARCHING" by Antonin Guttman
    /// </summary>
    public class RTree<TData> {
        /// <summary>
        /// 
        /// </summary>
        public RectangleNode<TData> RootNode
        {
            get { return _rootNode; }
            set { _rootNode=value; }
        }

        RectangleNode<TData> _rootNode;
       

        /// <summary>
        /// Create the query tree for a given enumerable of TData keyed by Rectangles
        /// </summary>
        /// <param name="rectsAndData"></param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1006:DoNotNestGenericTypesInMemberSignatures")]
        public RTree(IEnumerable<KeyValuePair<Rectangle, TData>> rectsAndData) {
            _rootNode = RectangleNode<TData>.CreateRectangleNodeOnEnumeration(GetNodeRects(rectsAndData));
        }

        /// <summary>
        /// Create a query tree for a given root node
        /// </summary>
        /// <param name="rootNode"></param>
        public RTree(RectangleNode<TData> rootNode) {
            this._rootNode = rootNode;
        }

        ///<summary>
        ///</summary>
        public RTree() {
            
        }

        /// <summary>
        /// The number of data elements in the tree (number of leaf nodes)
        /// </summary>
        public int Count {
            get { return _rootNode == null ? 0 : _rootNode.Count; }
        }

     
        /// <summary>
        /// Add the given key, value pair
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void Add(Rectangle key, TData value) {
            Add(new RectangleNode<TData>(value, key));            
        }

        internal void Add(RectangleNode<TData> node) {
            if (_rootNode == null)
                _rootNode = node;
            else if (Count <= 2)
                _rootNode = RectangleNode<TData>.CreateRectangleNodeOnEnumeration(_rootNode.GetAllLeafNodes().Concat(new[] {node}));
            else
                AddNodeToTreeRecursive(node, _rootNode);
        }
        /// <summary>
        /// rebuild the whole tree
        /// </summary>
        public void Rebuild() {
            _rootNode = RectangleNode<TData>.CreateRectangleNodeOnEnumeration(_rootNode.GetAllLeafNodes());
        }

        static IEnumerable<RectangleNode<TData>> GetNodeRects(IEnumerable<KeyValuePair<Rectangle, TData>> nodes) {
            return nodes.Select(v => new RectangleNode<TData>(v.Value, v.Key));
        }

        static void AddNodeToTreeRecursive(RectangleNode<TData> newNode, RectangleNode<TData> existingNode) {
            if (existingNode.IsLeaf) {
                existingNode.Left = new RectangleNode<TData>(existingNode.UserData, existingNode.Rectangle);
                existingNode.Right = newNode;
                existingNode.Count = 2;
                existingNode.UserData = default(TData);                
            } else {
                existingNode.Count++;
                Rectangle leftBox;
                Rectangle rightBox;
                if (2 * existingNode.Left.Count < existingNode.Right.Count) {
                    //keep the balance
                    AddNodeToTreeRecursive(newNode, existingNode.Left);
                    existingNode.Left.Rectangle = new Rectangle(existingNode.Left.Rectangle, newNode.Rectangle);
                } else if (2 * existingNode.Right.Count < existingNode.Left.Count) {
                    //keep the balance
                    AddNodeToTreeRecursive(newNode, existingNode.Right);
                    existingNode.Right.Rectangle = new Rectangle(existingNode.Right.Rectangle, newNode.Rectangle);
                } else { //decide basing on the boxes
                    leftBox = new Rectangle(existingNode.Left.Rectangle, newNode.Rectangle);
                    var delLeft = leftBox.Area - existingNode.Left.Rectangle.Area;
                    rightBox = new Rectangle(existingNode.Right.Rectangle, newNode.Rectangle);
                    var delRight = rightBox.Area - existingNode.Right.Rectangle.Area;
                    if (delLeft < delRight) {
                        AddNodeToTreeRecursive(newNode, existingNode.Left);
                        existingNode.Left.Rectangle = leftBox;
                    } else if(delLeft>delRight){
                        AddNodeToTreeRecursive(newNode, existingNode.Right);
                        existingNode.Right.Rectangle = rightBox;
                    } else { //the deltas are the same; add to the smallest
                        if(leftBox.Area<rightBox.Area) {
                            AddNodeToTreeRecursive(newNode, existingNode.Left);
                            existingNode.Left.Rectangle = leftBox;
                        }else {
                            AddNodeToTreeRecursive(newNode, existingNode.Right);
                            existingNode.Right.Rectangle = rightBox;
                        }
                    }
                }
            }
            existingNode.Rectangle = new Rectangle(existingNode.Left.Rectangle, existingNode.Right.Rectangle);
        }


        /// <summary>
        /// return all the data elements stored at the leaves of the BSPTree in an IEnumerable
        /// </summary>
        /// <returns></returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public IEnumerable<TData> GetAllLeaves() {
            return _rootNode!=null && Count>0 ? _rootNode.GetAllLeaves():new TData[0];
        }

        /// <summary>
        /// Get all data items with rectangles intersecting the specified rectangular region
        /// </summary>
        /// <param name="queryRegion"></param>
        /// <returns></returns>
        public TData[] GetAllIntersecting(Rectangle queryRegion)
        {
            return _rootNode == null || Count == 0 ? new TData[0] : _rootNode.GetNodeItemsIntersectingRectangle(queryRegion).ToArray();
        }

        public bool OneIntersecting(Rectangle queryRegion, out TData intersectedLeaf) {
            if (_rootNode == null || Count == 0) {
                intersectedLeaf = default(TData);
                return false;
            }
            RectangleNode<TData> ret = _rootNode.FirstIntersectedNode(queryRegion);
            if (ret == null) {
                intersectedLeaf = default(TData);
                return false;
            }
            intersectedLeaf = ret.UserData;
            return true;
        }

        /// <summary>
        /// Get all leaf nodes with rectangles intersecting the specified rectangular region
        /// </summary>
        /// <param name="queryRegion"></param>
        /// <returns></returns>
        internal IEnumerable<RectangleNode<TData>> GetAllLeavesIntersectingRectangle(Rectangle queryRegion) {
            return _rootNode == null || Count == 0 ? new RectangleNode<TData>[0] : _rootNode.GetLeafRectangleNodesIntersectingRectangle(queryRegion);
        }

        /// <summary>
        /// Does minimal work to determine if any objects in the tree intersect with the query region
        /// </summary>
        /// <param name="queryRegion"></param>
        /// <returns></returns>
        public bool IsIntersecting(Rectangle queryRegion) {
            return GetAllIntersecting(queryRegion).Any();
        }

        /// <summary>
        /// return true iff there is a node with the rectangle and UserData that equals to the parameter "userData"
        /// </summary>
        /// <param name="rectangle"></param>
        /// <param name="userData"></param>
        /// <returns></returns>
        public bool Contains(Rectangle rectangle, TData userData) {
            if (_rootNode == null) return false;
            return
                _rootNode.GetLeafRectangleNodesIntersectingRectangle(rectangle)
                        .Any(node => node.UserData.Equals(userData));
        }

        ///<summary>
        ///</summary>
        ///<param name="rectangle"></param>
        ///<param name="userData"></param>
        ///<returns></returns>
        public TData Remove(Rectangle rectangle, TData userData) {
            if (_rootNode==null)
            {
                return default(TData);
            }
            var ret = _rootNode.GetLeafRectangleNodesIntersectingRectangle(rectangle).FirstOrDefault(node => node.UserData.Equals(userData));
            if (ret == null)
                return default(TData);
            if (RootNode.Count == 1)
                RootNode = null;
            else
                RemoveLeaf(ret);
            return ret.UserData;
        }

        void RemoveLeaf(RectangleNode<TData> leaf) {
            Debug.Assert(leaf.IsLeaf);
            
            var unbalancedNode = FindTopUnbalancedNode(leaf);
            if (unbalancedNode != null) {
                RebuildUnderNodeWithoutLeaf(unbalancedNode, leaf);
                UpdateParent(unbalancedNode);
            } else {
                //replace the parent with the sibling and update bounding boxes and counts
                var parent = leaf.Parent;
                if (parent == null) {
                    Debug.Assert(_rootNode == leaf);
                    _rootNode = new RectangleNode<TData>();
                } else {
                    TransferFromSibling(parent, leaf.IsLeftChild ? parent.Right : parent.Left);
                    UpdateParent(parent);
                }
            }
        //   Debug.Assert(TreeIsCorrect(RootNode));
        }

//        static bool TreeIsCorrect(RectangleNode<TData> node)
//        {
//            if (node == null)
//                return true;
//            bool ret= node.Left != null && node.Right != null  ||
//                   node.Left == null && node.Right == null;
//            if (!ret)
//                return false;
//            return TreeIsCorrect(node.Left) && TreeIsCorrect(node.Right);
//        }

        static void UpdateParent(RectangleNode<TData> parent) {
            for(var node=parent.Parent; node!=null; node=node.Parent) {
                node.Count--;
                node.Rectangle=new Rectangle(node.Left.Rectangle, node.Right.Rectangle);
            }
        } 

        static void TransferFromSibling(RectangleNode<TData> parent, RectangleNode<TData> sibling) {
            parent.UserData=sibling.UserData;
            parent.Left = sibling.Left;
            parent.Right=sibling.Right;
            parent.Count--;
            parent.Rectangle = sibling.Rectangle;
        }

        static void RebuildUnderNodeWithoutLeaf(RectangleNode<TData> nodeForRebuild, RectangleNode<TData> leaf)
        {
            Debug.Assert(leaf.IsLeaf);
            Debug.Assert(!nodeForRebuild.IsLeaf);
            var newNode =
                RectangleNode<TData>.CreateRectangleNodeOnEnumeration(
                    nodeForRebuild.GetAllLeafNodes().Where(n => !(n.Equals(leaf))));
            nodeForRebuild.Count = newNode.Count;
            nodeForRebuild.Left = newNode.Left;
            nodeForRebuild.Right = newNode.Right;
            nodeForRebuild.Rectangle = new Rectangle(newNode.Left.rectangle, newNode.Right.rectangle);
        }

        static RectangleNode<TData> FindTopUnbalancedNode(RectangleNode<TData> node) {
            for (var parent = node.Parent; parent != null; parent = parent.Parent)
                if (! Balanced(parent))
                    return parent;
            return null;
        }

        static bool Balanced(RectangleNode<TData> rectangleNode) {
            return 2*rectangleNode.Left.Count >= rectangleNode.Right.Count &&
                   2*rectangleNode.Right.Count >= rectangleNode.Left.Count;
        }

        /// <summary>
        /// Removes everything from the tree
        /// </summary>
        public void Clear() {
            RootNode = null;
        }

        public bool NumberOfIntersectedIsLessThanBound(Rectangle rect, int bound, Func<TData, bool> conditionFunc ) {
            return NumberOfIntersectedIsLessThanBoundOnNode(_rootNode, rect, ref bound, conditionFunc);
        }

        static bool NumberOfIntersectedIsLessThanBoundOnNode(RectangleNode<TData> node, Rectangle rect, ref int bound, Func<TData, bool> conditionFunc) {
            Debug.Assert(bound > 0);
            if (!node.Rectangle.Intersects(rect)) return true;
            if (node.IsLeaf) {
                if (conditionFunc(node.UserData))
                    return (--bound) != 0;
                return true;
            }

            return NumberOfIntersectedIsLessThanBoundOnNode(node.Left, rect, ref bound, conditionFunc) &&
                   NumberOfIntersectedIsLessThanBoundOnNode(node.Right, rect, ref bound, conditionFunc);

        }
    }

}

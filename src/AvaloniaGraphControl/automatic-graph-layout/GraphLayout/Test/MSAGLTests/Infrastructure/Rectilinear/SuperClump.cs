﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="OverlappingGroupANdClumpMap.cs" company="Microsoft">
//   (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;

using Microsoft.Msagl.Core.DataStructures;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Routing.Rectilinear;

namespace Microsoft.Msagl.UnitTests.Rectilinear {
    /// <summary>
    /// This is a combination of one or more groups and any clumps they intersect.
    /// </summary>
    internal class SuperClump 
    {
        private readonly Set<Obstacle> groupsAndClumps = new Set<Obstacle>();
        private RectangleNode<Obstacle> hierarchy;

        internal void Insert(Obstacle obstacle) 
        {
            groupsAndClumps.Insert(obstacle);
        }

        internal IEnumerable<Obstacle> Obstacles { get { return this.groupsAndClumps; } }

        internal void CalculateHierarchy()
        {
            this.hierarchy = ObstacleTree.CalculateHierarchy(groupsAndClumps);
        }

        internal Rectangle Rectangle { get { return this.hierarchy.Rectangle; } }

        internal bool LandlocksPoint(Point location)
        {
            return LandlocksPoint(this.hierarchy, location);
        }

        internal static bool LandlocksPoint(RectangleNode<Obstacle> root, Point location) 
        {
            var bbox = root.Rectangle;
            if (!bbox.Contains(location)) 
            {
                return false;
            }

            // This is just a simple test; a more robust one would actually look for a bendy path.);
            return IntersectsSegment(root, location, StaticGraphUtility.RectangleBorderIntersect(bbox, location, Directions.North))
                || IntersectsSegment(root, location, StaticGraphUtility.RectangleBorderIntersect(bbox, location, Directions.South))
                || IntersectsSegment(root, location, StaticGraphUtility.RectangleBorderIntersect(bbox, location, Directions.East))
                || IntersectsSegment(root, location, StaticGraphUtility.RectangleBorderIntersect(bbox, location, Directions.West));
        }

        internal static bool IntersectsSegment(RectangleNode<Obstacle> root, Point start, Point end) 
        {
            return root.GetLeafRectangleNodesIntersectingRectangle(new Rectangle(start, end)).Any();
        }
    }
}
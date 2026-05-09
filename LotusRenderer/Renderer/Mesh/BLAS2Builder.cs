using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using LotusRenderer.Renderer.Types;

// I'm also working in an agnostic BVH C# library for Unity and plain C#.
// I plan to integrate it with this renderer in the future, so a refactor of all BVH related code
// is expected

public class BLAS2Builder
{
    public class BuildNode
    {
        public Vector3 Min;
        public Vector3 Max;
        public int FirstTriIdx; // Index into global indices array
        public int FirstChildIdx;
        public int TriCount;
        public int TotalChildren = 0;
        public BuildNode? Left;
        public BuildNode? Right;
        public bool IsLeaf = true;
    }
    
    private struct TriBounds
    {
        public int OrigIndex;
        public Vector3 Centroid;
        public Vector3 Min;
        public Vector3 Max;
    }
    
    public BVH2Node[] OutNodes;
    public uint[] ReorderedIndices;
    public BuildNode root;
    
    private TriBounds[] _triBounds;

    public void Build(ref PackedVertex[] vertices, ref uint[] origTriIndices)
    {
        PreBuildTriangleBounds(vertices, origTriIndices);

        
        root = BuildRecursive(0, _triBounds.Length);
        List<BuildNode> flattenBuildNodes = new List<BuildNode> { root };
        FlattenTree(flattenBuildNodes, root);
        BuildFinalNodesAndTriangles(flattenBuildNodes, ref origTriIndices);
    }

    private void FlattenTree(List<BuildNode> result, BuildNode current)
    {
        if(current.IsLeaf)
            return;
        
        current.FirstChildIdx = result.Count;
        result.Add(current.Left);
        result.Add(current.Right);
        
        if(!current.Left.IsLeaf)
            FlattenTree(result, current.Left);
        if(!current.Right.IsLeaf)
            FlattenTree(result, current.Right);
    }

    private void BuildFinalNodesAndTriangles(List<BuildNode> buildNodes, ref uint[] origTriIndices)
    {
        OutNodes = new BVH2Node[buildNodes.Count];
        ReorderedIndices = new uint[origTriIndices.Length];
        int sortedTs = 0;

        for (int n = 0; n < buildNodes.Count; n++)
        {
            var bNode = buildNodes[n];
            
            BVH2Node flatNode = new BVH2Node();
            flatNode.BoundMin = bNode.Min;
            flatNode.BoundMax = bNode.Max;

            if (bNode.IsLeaf)
            {
                flatNode.TriCount = bNode.TriCount;
                flatNode.FirstChildIdx = sortedTs * 3;

                for (int t = 0; t < bNode.TriCount; t++)
                {
                    var tBounds = _triBounds[bNode.FirstTriIdx + t];
                    int origIdx = tBounds.OrigIndex;
                    ReorderedIndices[sortedTs * 3 + 0] = origTriIndices[origIdx * 3 + 0];
                    ReorderedIndices[sortedTs * 3 + 1] = origTriIndices[origIdx * 3 + 1];
                    ReorderedIndices[sortedTs * 3 + 2] = origTriIndices[origIdx * 3 + 2];
                    sortedTs++;
                }
            }
            else
            {
                flatNode.TriCount = 0;
                flatNode.FirstChildIdx = bNode.FirstChildIdx;
            }      
            
            OutNodes[n] = flatNode;
        }
    }
    

    private void PreBuildTriangleBounds(in PackedVertex[] vertices, in uint[] triIndices)
    {
        int triCount = triIndices.Length / 3;
        _triBounds = new TriBounds[triCount];

        for (int i = 0; i < triCount; i++)
        {
            uint i0 = triIndices[i * 3 + 0];
            uint i1 = triIndices[i * 3 + 1];
            uint i2 = triIndices[i * 3 + 2];

            Vector3 p0 = vertices[i0].Position;
            Vector3 p1 = vertices[i1].Position;
            Vector3 p2 = vertices[i2].Position;

            _triBounds[i].OrigIndex = i; // Original Triangle Index (0..N)
            _triBounds[i].Min = Vector3.Min(p0, Vector3.Min(p1, p2));
            _triBounds[i].Max = Vector3.Max(p0, Vector3.Max(p1, p2));
            _triBounds[i].Centroid = (p0 + p1 + p2) * (1.0f / 3.0f);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float SurfaceArea(in Vector3 e) => e.X * e.Y + e.Y * e.Z + e.Z * e.X;
    
    private BuildNode BuildRecursive( int triStart, int triCount)
    {
        BuildNode node = new BuildNode();
        
        var boundsMin = new Vector3(float.MaxValue);
        var boundsMax = new Vector3(float.MinValue);
        var cenMin = new Vector3(float.MaxValue);
        var cenMax = new Vector3(float.MinValue); 
        
        for (int i = 0; i < triCount; i++)
        {
            var tri = _triBounds[triStart + i];
            boundsMin = Vector3.Min(boundsMin, tri.Min);
            boundsMax = Vector3.Max(boundsMax, tri.Max);
            cenMin = Vector3.Min(cenMin, tri.Centroid);
            cenMax = Vector3.Max(cenMax, tri.Centroid);
        }
        
        node.Min = boundsMin;
        node.Max = boundsMax;
        
        if (triCount <= 2) // Typically 2-4
        {
            node.FirstTriIdx = triStart;
            node.TriCount = triCount;
            return node;
        }
        
        // SAH Logic
        int bestAxis = -1;
        float bestSplitPos = 0;
        float bestCost = float.MaxValue;
        
        // Find longest axis
        Vector3 extent = boundsMax - boundsMin;
        

        // for each axis, get cost and choose best one to divide the node
        // and at which point we should split
        for (int axis = 0; axis < 3; axis++)
        {
            float axisExt = extent[axis];

            // we skip dividing the axis if its extends are too tiny
            if (axisExt < 0.001f)
                continue;
            
            // this is an experiment, instead of fixing a value for bin count
            // I adapt it to the extends of the axis of this node by splits of 5 cms
            // with min max of count
            int BIN_COUNT = 8;
            
            float axisMin = cenMin[axis];
            float axisMax = cenMax[axis];
            // the scale of each bin  
            float scale = BIN_COUNT / (axisMax - axisMin);
            
            var binCount = new int[BIN_COUNT];
            var binBoundsMin = new Vector3[BIN_COUNT];
            var binBoundsMax = new Vector3[BIN_COUNT];
            
            // init binds
            for (int b = 0; b < BIN_COUNT; b++)
            {
                binBoundsMin[b] = new Vector3(float.MaxValue);
                binBoundsMax[b] = new Vector3(float.MinValue);
            }
            
            // populate bins
            for (int b = 0; b < triCount; b++)
            {
                var tri = _triBounds[triStart + b];
                // finding into which bin this tri would fall
                int binIdx = (int)((tri.Centroid[axis] - axisMin) * scale);
                binIdx = Math.Clamp(binIdx, 0, BIN_COUNT - 1);
                // we add the count of how many tris in the bin
                // and expand the bounds of the bin with this triangle's bounds
                binCount[binIdx]++;
                binBoundsMin[binIdx] = Vector3.Min(binBoundsMin[binIdx], tri.Min);
                binBoundsMax[binIdx] = Vector3.Max(binBoundsMax[binIdx], tri.Max);
            }
            
            // now that we have all the bins populated for this axis,
            // we have to go through each bin, temporary construct the potential
            // new bound if we would split there and calculate what its score would be
            // so we can choose later the one with the best score
            for (int i = 0; i < BIN_COUNT - 1; i++)
            {
                
                int countLeft = 0;
                int countRight = 0;
                Vector3 boxLeftMin = new Vector3(float.MaxValue);
                Vector3 boxLeftMax = new Vector3(float.MinValue);
                Vector3 boxRightMin = new Vector3(float.MaxValue);
                Vector3 boxRightMax = new Vector3(float.MinValue);

                // for this bin's split, how many triangles to the left
                // and what would be the left resulting bound
                for (int b = 0; b <= i; b++)
                {
                    countLeft += binCount[b];
                    if (binCount[b] > 0)
                    {
                        boxLeftMin = Vector3.Min(boxLeftMin, binBoundsMin[b]);
                        boxLeftMax = Vector3.Max(boxLeftMax, binBoundsMax[b]); 
                    }
                }
                
                // for this bin's split, how many triangles to the right
                // and what would be the right resulting bound
                for (int b = i + 1; b < BIN_COUNT; b++) 
                {
                    countRight += binCount[b];
                    if(binCount[b] > 0) 
                    {
                        boxRightMin = Vector3.Min(boxRightMin, binBoundsMin[b]);
                        boxRightMax = Vector3.Max(boxRightMax, binBoundsMax[b]);
                    }
                }
                
                // we skip splits that cause a single child to be created
                if(countLeft == 0 || countRight == 0)
                    continue;

                // there are different ways of calculating the split cost
                // but this one is quite standard
                float areaLeft = SurfaceArea(boxLeftMax - boxLeftMin);
                float areaRight = SurfaceArea(boxRightMax - boxRightMin);
                float cost = countLeft * areaLeft + countRight * areaRight;

                // the lower the cost the better, since this is a heuristic
                // of how much it will cost to most rays to traverse this split
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestAxis = axis;
                    bestSplitPos = axisMin + (i + 1) * (axisMax - axisMin) / BIN_COUNT;
                }
            }
        }
        // end of finding best split

        // if we failed to find a split or its score is not worth to split
        // we make this node a leaf and stop subdividing
        float parentArea = SurfaceArea(extent);
        float leafCost = triCount * parentArea;
        if (bestAxis == -1 || bestCost >= leafCost)
        {
            node.FirstTriIdx = triStart;
            node.TriCount = triCount;
            return node;
        }

        // re-organize the triangle bounds so they fall
        // into their respective new nodes
        int leftStartIdx = triStart;
        int rightStartIdx = triStart + triCount - 1;
        while (leftStartIdx <= rightStartIdx)
        {
            float bestAxisCentroid = _triBounds[leftStartIdx].Centroid[bestAxis];
            if (bestAxisCentroid < bestSplitPos)
            {
                leftStartIdx++;
            }
            else
            {
                // swap
                (_triBounds[leftStartIdx], _triBounds[rightStartIdx]) = (_triBounds[rightStartIdx], _triBounds[leftStartIdx]);
                rightStartIdx--;
            }
        }
        
        // another scenario where we don't want to continue with the subdivisions
        // is if one of the splits contains no triangles at all
        int leftCount = leftStartIdx - triStart;
        if (leftCount == 0 || leftCount == triCount)
        {
            node.FirstTriIdx = triStart;
            node.TriCount = triCount;
            return node;
        }

       
        node.Left = BuildRecursive(triStart, leftCount);
        node.Right = BuildRecursive(leftStartIdx, triCount - leftCount);
        // no self hosted triangles but takes count of all inside
        node.TriCount = triCount; 
        node.TotalChildren = 2 + node.Left.TotalChildren + node.Right.TotalChildren;
        node.IsLeaf = false;
        
        return node;
    }
}
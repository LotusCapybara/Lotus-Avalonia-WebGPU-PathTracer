using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using LotusRenderer.Renderer.Types; 

// I'm also working in an agnostic BVH C# library for Unity and plain C#.
// I plan to integrate it with this renderer in the future, so a refactor of all BVH related code
// is expected

namespace LotusRenderer.Renderer.World
{
    public class TLAS2Builder
    {
        public class BuildNode
        {
            public Vector3 Min;
            public Vector3 Max;
            public int FirstMiIndex;
            public int FirstChildIdx;
            public int MiCount;
            public int TotalChildren = 0;
            public BuildNode? Left;
            public BuildNode? Right;
            public bool IsLeaf = true;
        }
        
        private struct MeshBounds
        {
            public Vector3 Min;
            public Vector3 Max;
            public Vector3 Centroid;
            public int OriginalInstanceIndex; 
        }

        public BVH2Node[] OutNodes;
        public MeshInstance[] ReorderedInstances; 
        public BuildNode root;

        private MeshBounds[] _workBounds;
        
        public void Build(ref MeshInstance[] meshInstances, MeshInfo[] meshInfos)
        {
            PreBuildMeshBounds(ref meshInstances, meshInfos);
            root = BuildRecursive(0, _workBounds.Length);
            List<BuildNode> flattenBuildNodes = new List<BuildNode> { root };
            FlattenTree(flattenBuildNodes, root);
            BuildFinalNodesAndInstances(flattenBuildNodes, ref meshInstances);
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
        
        private void BuildFinalNodesAndInstances(List<BuildNode> buildNodes, ref MeshInstance[] origMeshInstances)
        {
            OutNodes = new BVH2Node[buildNodes.Count];
            ReorderedInstances = new MeshInstance[origMeshInstances.Length];
            int sortedMs = 0;

            for (int n = 0; n < buildNodes.Count; n++)
            {
                var bNode = buildNodes[n];
            
                BVH2Node flatNode = new BVH2Node();
                flatNode.BoundMin = bNode.Min;
                flatNode.BoundMax = bNode.Max;

                if (bNode.IsLeaf)
                {
                    flatNode.TriCount = bNode.MiCount;
                    flatNode.FirstChildIdx = sortedMs;

                    for (int t = 0; t < bNode.MiCount; t++)
                    {
                        var tBounds = _workBounds[bNode.FirstMiIndex + t];
                        int origIdx = tBounds.OriginalInstanceIndex;
                        ReorderedInstances[sortedMs++] = origMeshInstances[origIdx];
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
        

        private void PreBuildMeshBounds(ref MeshInstance[] meshInstances, MeshInfo[] meshInfos)
        {
            _workBounds = new MeshBounds[meshInstances.Length];

            for (int i = 0; i < meshInstances.Length; i++)
            {
                var meshDef = meshInfos[meshInstances[i].MeshInfoId];
                var mat = meshInstances[i].Transform;

                var localMin = meshDef.BoundMin;
                var localMax = meshDef.BoundMax;

                Vector3 worldMin = new Vector3(float.PositiveInfinity);
                Vector3 worldMax = new Vector3(float.NegativeInfinity);

                // 8 corners of the local AABB
                for (int c = 0; c < 8; ++c)
                {
                    var local = new Vector3(
                        ((c & 1) != 0) ? localMax.X : localMin.X,
                        ((c & 2) != 0) ? localMax.Y : localMin.Y,
                        ((c & 4) != 0) ? localMax.Z : localMin.Z
                    );

                    // System.Numerics: row-major, this is fine
                    var world = Vector3.Transform(local, mat);

                    worldMin = Vector3.Min(worldMin, world);
                    worldMax = Vector3.Max(worldMax, world);
                }

                // Small padding
                float padding = 0.001f;
                worldMin -= new Vector3(padding);
                worldMax += new Vector3(padding);

                _workBounds[i].Min = worldMin;
                _workBounds[i].Max = worldMax;
                _workBounds[i].Centroid = worldMin + (worldMax - worldMin) * 0.5f;
                _workBounds[i].OriginalInstanceIndex = i;
            }
        }

        private void TransformAxis(float m1, float m2, float m3, float min, float max, ref Vector3 outMin, ref Vector3 outMax)
        {
            if (m1 > 0) { outMin.X += m1 * min; outMax.X += m1 * max; }
            else        { outMin.X += m1 * max; outMax.X += m1 * min; }

            if (m2 > 0) { outMin.Y += m2 * min; outMax.Y += m2 * max; }
            else        { outMin.Y += m2 * max; outMax.Y += m2 * min; }

            if (m3 > 0) { outMin.Z += m3 * min; outMax.Z += m3 * max; }
            else        { outMin.Z += m3 * max; outMax.Z += m3 * min; }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float SurfaceArea(in Vector3 e) => e.X * e.Y + e.Y * e.Z + e.Z * e.X;

        private BuildNode BuildRecursive(int miStart, int miCount)
        {
            // Create a new node
            BuildNode node = new BuildNode();
            
            var boundsMin = new Vector3(float.MaxValue);
            var boundsMax = new Vector3(float.MinValue);
            
            for (int i = 0; i < miCount; i++)
            {
                var miBounds = _workBounds[miStart + i];
                boundsMin = Vector3.Min(boundsMin, miBounds.Min);
                boundsMax = Vector3.Max(boundsMax, miBounds.Max);
            }

            node.Min = boundsMin;
            node.Max = boundsMax;

            if (miCount <= 2) // Typically 2-4
            {
                node.FirstMiIndex = miStart;
                node.MiCount = miCount;
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
                int BIN_COUNT = (int) Math.Clamp(axisExt / 0.05f, 8, 128);
                
                float axisMin = boundsMin[axis];
                float axisMax = boundsMax[axis];
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
                for (int b = 0; b < miCount; b++)
                {
                    var tri = _workBounds[miStart + b];
                    // finding into which bin this mesh instance would fall
                    int binIdx = (int)((tri.Centroid[axis] - axisMin) * scale);
                    binIdx = Math.Clamp(binIdx, 0, BIN_COUNT - 1);
                    // we add the count of how many mesh instances in the bin
                    // and expand the bounds of the bin with mesh instance's bounds
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

                    // for this bin's split, how many meshes to the left
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
                    
                    // for this bin's split, how many meshes to the right
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
            float leafCost = miCount * parentArea;
            if (bestAxis == -1 || bestCost >= leafCost)
            {
                node.FirstMiIndex = miStart;
                node.MiCount = miCount;
                return node;
            }

            // re-organize the mesh bounds so they fall
            // into their respective new nodes
            int leftStartIdx = miStart;
            int rightStartIdx = miStart + miCount - 1;
            while (leftStartIdx <= rightStartIdx)
            {
                float bestAxisCentroid = _workBounds[leftStartIdx].Centroid[bestAxis];
                if (bestAxisCentroid < bestSplitPos)
                {
                    leftStartIdx++;
                }
                else
                {
                    // swap
                    (_workBounds[leftStartIdx], _workBounds[rightStartIdx]) = (_workBounds[rightStartIdx], _workBounds[leftStartIdx]);
                    rightStartIdx--;
                }
            }
            
            // another scenario where we don't want to continue with the subdivisions
            // is if one of the splits contains no meshes at all
            int leftCount = leftStartIdx - miStart;
            if (leftCount == 0 || leftCount == miCount)
            {
                node.FirstMiIndex = miStart;
                node.MiCount = miCount;
                return node;
            }

            node.Left = BuildRecursive(miStart, leftCount);
            node.Right = BuildRecursive(leftStartIdx, miCount - leftCount);
            // no self hosted meshes but takes count of all inside
            node.MiCount = miCount; 
            node.TotalChildren = 2 + node.Left.TotalChildren + node.Right.TotalChildren;
            node.IsLeaf = false;
            
            return node;
        }
    }
}
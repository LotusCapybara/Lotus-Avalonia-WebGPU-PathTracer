using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LotusRenderer.Renderer.Mesh;
using LotusRenderer.Renderer.Types;
using LotusRenderer.Renderer.World;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;
using Material = LotusRenderer.Renderer.Types.Material;
using Scene = LotusRenderer.Renderer.World.Scene;

public static class MeshLoader
{
    public static void LoadAndParseGLBScene(string filename, Scene scene, int matIndexOffset)
    {
        var parsedFile = LoadGLB(filename, matIndexOffset, scene);
        ParseMeshDatas(parsedFile.Item1, parsedFile.Item2, parsedFile.Item3, scene);
        BuildAccelerationStructures(scene);
        BuildEmissiveTriangleList(scene);
        PrepareDataArrays(scene);
    }

    private static void BuildAccelerationStructures(Scene scene)
    {
        // we have to build one bvh tree per mesh definition, so they are local
        // and we sort them in the buffer
        uint bvhIdxOffset = 0;
        List<BVH2Node> tmpStackNodes = new List<BVH2Node>();
        for (int m = 0; m < scene.MeshInfos.Length; m++)
        {
            var mInfo = scene.MeshInfos[m];
            uint[] meshTriangles = new uint[mInfo.qtyTriIdx];
            Array.Copy(scene.TriangleIndices, (int) mInfo.tIndexOffset, meshTriangles, 0, (int)mInfo.qtyTriIdx);
            BLAS2Builder blas2Builder = new BLAS2Builder();
            blas2Builder.Build(ref scene.PackedVertices, ref meshTriangles); 
            tmpStackNodes.AddRange(blas2Builder.OutNodes);
            Array.Copy(blas2Builder.ReorderedIndices, 0, scene.TriangleIndices, (int) mInfo.tIndexOffset, (int)mInfo.qtyTriIdx);

            mInfo.BoundMin = blas2Builder.root.Min;
            mInfo.BoundMax = blas2Builder.root.Max;
            mInfo.qtyBVHNodes = (uint)blas2Builder.OutNodes.Length;
            mInfo.treeRootIdx = bvhIdxOffset;
            scene.MeshInfos[m] = mInfo;
            
            bvhIdxOffset += mInfo.qtyBVHNodes;
        }
        
        scene.BLASNodes = tmpStackNodes.ToArray();

        var tlasBuilder = new TLAS2Builder();
        tlasBuilder.Build( ref scene.MeshInstances, scene.MeshInfos);
        scene.TLASNodes = tlasBuilder.OutNodes;
        scene.MeshInstances = tlasBuilder.ReorderedInstances;
    }
    
    

    private static void BuildEmissiveTriangleList(Scene scene)
    {
        scene.TotalEmissiveArea = 0f;
        
        var emissives = new List<EmissiveTriangle>();

        for (int i = 0; i < scene.MeshInstances.Length; i++)
        {
            var inst = scene.MeshInstances[i];
            var info = scene.MeshInfos[inst.MeshInfoId];
            var mat = scene.Materials[info.matId];
            
            if(mat.EmissionStrength <= 0.0f)
                continue;

            uint triCount = info.qtyTriIdx / 3;

            for (uint t = 0; t < triCount; t++)
            {
                uint globalTriOffset = info.tIndexOffset + t * 3;
                
                uint i0 = scene.TriangleIndices[globalTriOffset + 0];
                uint i1 = scene.TriangleIndices[globalTriOffset + 1];
                uint i2 = scene.TriangleIndices[globalTriOffset + 2];

                Vector3 p0Local = scene.PackedVertices[i0].Position;
                Vector3 p1Local = scene.PackedVertices[i1].Position;
                Vector3 p2Local = scene.PackedVertices[i2].Position;
                
                Vector3 p0 = Vector3.Transform(p0Local, inst.Transform);
                Vector3 p1 = Vector3.Transform(p1Local, inst.Transform);
                Vector3 p2 = Vector3.Transform(p2Local, inst.Transform);

                Vector2 uv0 = new Vector2(scene.PackedVertices[i0].U, scene.PackedVertices[i0].V);
                Vector2 uv1 = new Vector2(scene.PackedVertices[i1].U, scene.PackedVertices[i1].V);
                Vector2 uv2 = new Vector2(scene.PackedVertices[i2].U, scene.PackedVertices[i2].V);
                
                float area = Vector3.Cross(p1 - p0, p2 - p0).Length() * 0.5f;
                scene.TotalEmissiveArea += area;

                // skipping triangles with really low area, I assume they don't contribute 
                // to lighting much anyways
                if (area < 1e-8f)
                    continue;
                
                emissives.Add(new EmissiveTriangle
                {
                    meshInstanceIdx = (uint)i,
                    triIndexOffset = globalTriOffset,
                    area = area,
                    uv0 = uv0,
                    uv1 = uv1,
                    uv2 = uv2,
                });
            }

        }

        scene.EmissiveTriangles = emissives.ToArray();
    }
    
    private static void PrepareDataArrays(Scene scene)
    {
        var NextData = scene.Data;
        NextData.QtyMaterials = scene.Materials.Length;
        NextData.QtyMeshes = scene.MeshInfos.Length;
        NextData.QtyMeshInstances = scene.MeshInstances.Length;
        NextData.TotalVertices = scene.PackedVertices.Length;
        NextData.TotalTriIndices = scene.TriangleIndices.Length;
        NextData.QtyEmissiveTriangles = scene.EmissiveTriangles.Length;
        NextData.TotalEmissiveArea = scene.TotalEmissiveArea;
        scene.Data = NextData;
        
        // todo: create some UI to see scene metrics and stuff like that
        // and include this there. 
    }
    
    
    private static (List<MeshData>, List<Material>, List<MeshInstance>) LoadGLB(
        string filename, int matIndexOffset, Scene scene)
    {
        var allMeshes = new List<MeshData>();
        var glbModel = ModelRoot.Load(filename, new ReadSettings
        {
            Validation = ValidationMode.Skip
        });

        // we use matIndexOffset because some pre-existing materials in the scene could be there
        // so we want to be sure the newly added materials have the correct id offset
        var matsTuple = ParseMaterials(glbModel, matIndexOffset, scene);

        Dictionary<string, int> matIdxByName = matsTuple.Item1;
        List<Material> mats = matsTuple.Item2;

        if (mats.Count == 0)
        {
            mats.Add(MaterialFactoryHelper.CreateMatte(new Vector3(1f)));
            matIdxByName.Add("__Default__", 0);
        }

        foreach (var logicalMesh in glbModel.LogicalMeshes)
        {
            foreach (var prim in logicalMesh.Primitives)
            {
                if (prim.DrawPrimitiveType != PrimitiveType.TRIANGLES)
                    continue;

                var positions = prim.GetVertexAccessor("POSITION").AsVector3Array();
                var normalsAccessor = prim.GetVertexAccessor("NORMAL");
                var tangentsAccessor = prim.GetVertexAccessor("TANGENT");
                var uvAccessor = prim.GetVertexAccessor("TEXCOORD_0");

                var normals = normalsAccessor?.AsVector3Array();
                var uvs = uvAccessor?.AsVector2Array();
                var vertexCount = positions.Count;
                var packedVertices = new PackedVertex[vertexCount];
                var triIndices = prim.GetIndices().Select(idx => (uint)idx).ToArray();
                var tangents = tangentsAccessor?.AsVector4Array();

                Vector4[] finalTangents;
                if (tangents != null) {
                    finalTangents = tangents.ToArray();
                } else {
                    Vector2[] safeUVs = (uvs != null) ? uvs.ToArray() : new Vector2[vertexCount];
                    Vector3[] safeNormals = (normals != null) ? normals.ToArray() : new Vector3[vertexCount]; // Should handle if normals missing too
                    finalTangents = GenerateTangents(positions, safeNormals, safeUVs, triIndices);
                }
              
                for (int i = 0; i < vertexCount; i++)
                {
                    // Default Fallbacks: Normal=Up, UV=0,0
                    var vPos = positions[i];
                    var n = (normals != null) ? normals[i] : Vector3.UnitY;
                    var uv = (uvs != null) ? uvs[i] : Vector2.Zero;

                    packedVertices[i] = new PackedVertex
                    {
                        Position = vPos,
                        U = uv.X,
                        Normal = n,
                        V = uv.Y,
                        Tangent = finalTangents[i]
                    };
                }

                string matName = prim.Material == null ? "__Default__" : prim.Material.Name;
                int matId = matIdxByName[matName];
                int orderId = allMeshes.Count;

                allMeshes.Add(new MeshData
                {
                    Vertices = packedVertices,
                    Triangles = triIndices,
                    matId = (uint)matId,
                    LogicalIndex = logicalMesh.LogicalIndex,
                    OrderId = orderId
                });
            }
        }

        List<MeshInstance> meshInstances = new List<MeshInstance>();

        foreach (var node in glbModel.LogicalNodes)
        {
            if (node.Mesh == null)
                continue;

            var worldMatrix = node.WorldMatrix;
            Matrix4x4.Invert(worldMatrix, out var inverseMatrix);

            var gpuTransform = worldMatrix;
            var gpuInverse = inverseMatrix;

            var primitives = allMeshes.Where(m => m.LogicalIndex == node.Mesh.LogicalIndex).ToList();

            foreach (var prim in primitives)
            {
                var instance = new MeshInstance
                {
                    Transform = gpuTransform,
                    InverseTransform = gpuInverse,
                    MeshInfoId = (uint)prim.OrderId
                };
                meshInstances.Add(instance);
            }

        }

        return (allMeshes, mats, meshInstances);
    }

    private static (Dictionary<string, int>, List<Material> ) ParseMaterials(ModelRoot model, int matIndexOffset,
        Scene scene)
    {
        // Parsing Textures first
        var sourceImages = model.LogicalImages;
        var imgIndexMap = new Dictionary<int, int>();
        var imagesToProcess = new List<MemoryImage>();

        for (int i = 0; i < sourceImages.Count; i++)
        {
            var logicalImage = sourceImages[i];
            imagesToProcess.Add(logicalImage.Content);
            imgIndexMap[i] = i; // todo: some sort of mapping, atlassing, etc?
        }

        scene.RawTextureArray = TextureLoader.ProcessTextures(imagesToProcess, out int layerCount);
        scene.TextureLayersCount = (uint)layerCount;

        Dictionary<string, int> matIndexByName = new Dictionary<string, int>();
        List<Material> mats = new List<Material>();

        foreach (var glMat in model.LogicalMaterials)
        {
            if (matIndexByName.ContainsKey(glMat.Name))                                                                  
            {
                Console.WriteLine($"[MeshLoader] Warning: duplicate material name '{glMat.Name}', skipping.");         
                continue;                                                                                              
            }

            // Start with a default matte material
            // We'll override properties as we find them
            var mat = MaterialFactoryHelper.CreatePbr(Vector3.One, 0f, 1f, 0f);
            mat.textIdBaseColor = -1;
            mat.textIdEmission = -1;
            mat.textIdMetallic = -1;
            mat.textIdNormal = -1;
            mat.textIdRoughness = -1;
            mat.textIdTransmission = -1;
            mat.blendType = (uint)EBlendType.Opaque;
            mat.normalScale = 1f;

            // ----- Base Color
            var baseColorChannel = glMat.FindChannel("BaseColor");
            if (baseColorChannel.HasValue)
            {
                var color = baseColorChannel.Value.Color;
                mat.BaseColor = new Vector4(color.X, color.Y, color.Z, color.W);

                // try get texture for base color for material
                if (baseColorChannel.Value.Texture != null)
                {
                    int gltfTxtId = baseColorChannel.Value.Texture.PrimaryImage.LogicalIndex;
                    if (imgIndexMap.TryGetValue(gltfTxtId, out int layerIndex))
                    {
                        mat.textIdBaseColor = layerIndex;
                    }
                }
            }

            // ---------  Metallic / Roughness
            var metalRoughChannel = glMat.FindChannel("MetallicRoughness");
            if (metalRoughChannel.HasValue)
            {
                // GLTF packs Metallic in B and Roughness in G usually, 
                // but SharpGLTF exposes the parameters directly.
                mat.Metallic = metalRoughChannel.Value.GetFactor("MetallicFactor");
                mat.Roughness = metalRoughChannel.Value.GetFactor("RoughnessFactor");

                // Texture Extraction
                if (metalRoughChannel.Value.Texture != null)
                {
                    int gltfTxtId = metalRoughChannel.Value.Texture.PrimaryImage.LogicalIndex;

                    if (imgIndexMap.TryGetValue(gltfTxtId, out int layerIndex))
                    {
                        // They share the same texture slot
                        mat.textIdMetallic = layerIndex;
                        mat.textIdRoughness = layerIndex;
                    }
                }
            }


            // ----- emission
            mat.EmissionColor = Vector3.One;
            mat.EmissionStrength = 0f;

            var emissionChannel = glMat.FindChannel("Emissive");
            if (emissionChannel.HasValue)
            {
                var emit = emissionChannel.Value.Color;
                mat.EmissionColor = new Vector3(emit.X, emit.Y, emit.Z);

                // Logic: If the emission channel exists and has color, strength is 1.0 (unless black)
                if (emit.X > 0 || emit.Y > 0 || emit.Z > 0)
                {
                    mat.EmissionStrength = 1.0f;
                }

                if (emissionChannel.Value.Texture != null)
                {
                    int gltfTxtId = emissionChannel.Value.Texture.PrimaryImage.LogicalIndex;
                    if (imgIndexMap.TryGetValue(gltfTxtId, out int layerIndex))
                    {
                        mat.textIdEmission = layerIndex;
                        // Ensure strength is at least 1 so texture shows up
                        if (mat.EmissionStrength <= 0.0f) mat.EmissionStrength = 1.0f;
                    }
                }
            }


            var emissiveStrengthExt = glMat.Extensions?
                .FirstOrDefault(e => e.GetType().Name.Contains("EmissiveStrength"));

            if (emissiveStrengthExt != null)
            {
                // Use Reflection to read the 'EmissiveStrengthFactor' property
                var prop = emissiveStrengthExt.GetType().GetProperty("EmissiveStrength");
                if (prop != null)
                {
                    var factor = (float)prop.GetValue(emissiveStrengthExt);
                    mat.EmissionStrength =
                        mat.EmissionStrength > 0 ? mat.EmissionStrength * (float)factor : (float)factor;
                }
            }

            // D. Transmission (Glass)
            // SharpGLTF handles KHR_materials_transmission
            var transmissionChannel = glMat.FindChannel("Transmission");
            if (transmissionChannel.HasValue)
            {
                mat.TransmissionWeight = transmissionChannel.Value.GetFactor("TransmissionFactor");
            }
            else
            {
                var transmissionExt = glMat.Extensions?
                    .FirstOrDefault(e => e.GetType().Name.Contains("MaterialTransmission"));
                
                if (transmissionExt != null)
                {
                    var prop = transmissionExt.GetType().GetProperty("TransmissionFactor");
                    if (prop != null)
                    {
                        var factor = (float)prop.GetValue(transmissionExt);
                        mat.TransmissionWeight = factor;
                    }
                }
            }

            if (glMat.Alpha == AlphaMode.BLEND)
            {
                mat.blendType = (uint)EBlendType.Blend;
            }
            else if (glMat.Alpha == AlphaMode.MASK)
            {
                mat.blendType = (uint)EBlendType.Mask;
            }

        
            var normalChannel = glMat.FindChannel("Normal");
            if (normalChannel.HasValue)
            {
                if (normalChannel.Value.Texture != null)
                {
                    int gltfTxtId = normalChannel.Value.Texture.PrimaryImage.LogicalIndex;

                    if (imgIndexMap.TryGetValue(gltfTxtId, out int layerIndex))
                    {
                        mat.textIdNormal = layerIndex;
                        var normalScaleParameter = normalChannel.Value.Parameters.First( p => p.Name == "NormalScale");
                        mat.normalScale = (float) normalScaleParameter.Value;
                    }
                }
            }

            // E. IOR
            var iorChannel = glMat.FindChannel("IOR");
            if (iorChannel.HasValue)
            {
                mat.IOR = iorChannel.Value.GetFactor("IOR");
            }
            else
            {
                mat.IOR = glMat.IndexOfRefraction;
            }

            mat.matIndex = (uint)(mats.Count + matIndexOffset);
            matIndexByName.Add(glMat.Name, (int)mat.matIndex);
            mats.Add(mat);
        }

        return (matIndexByName, mats);
    }

    private static void ParseMeshDatas(List<MeshData> meshDatas, List<Material> materials, List<MeshInstance> meshInstances, Scene scene)
    {
        scene.MeshInfos = new MeshInfo[meshDatas.Count];

        uint totalVertices = 0;
        uint totalTriIdxs = 0;

        for (int m = 0; m < meshDatas.Count; m++)
        {
            MeshInfo meshInfo = new MeshInfo();
            meshInfo.qtyVertices = (uint)meshDatas[m].Vertices.Length;
            meshInfo.qtyTriIdx = (uint)meshDatas[m].Triangles.Length;
            meshInfo.matId = meshDatas[m].matId;
            meshInfo.tIndexOffset = totalTriIdxs;

            totalVertices += meshInfo.qtyVertices;
            totalTriIdxs += meshInfo.qtyTriIdx;

            scene.MeshInfos[m] = meshInfo;
        }

        scene.PackedVertices = new PackedVertex[totalVertices];
        scene.TriangleIndices = new uint[totalTriIdxs];

        int vOfsset = 0;
        int tOfsset = 0;

        for (int m = 0; m < meshDatas.Count; m++)
        {
            var meshData = meshDatas[m];
            Array.Copy(meshData.Vertices, 0, scene.PackedVertices, vOfsset, meshData.Vertices.Length);

            for (int ti = 0; ti < meshData.Triangles.Length; ti++)
            {
                scene.TriangleIndices[tOfsset + ti] = meshData.Triangles[ti] + (uint)vOfsset;
            }

            vOfsset += meshData.Vertices.Length;
            tOfsset += meshData.Triangles.Length;
        }

        List<Material> allMaterials = new List<Material>();
        allMaterials.AddRange(scene.Materials);
        allMaterials.AddRange(materials);
        scene.Materials = allMaterials.ToArray();

        scene.MeshInstances = meshInstances.ToArray();
    }
    
    private static Vector4[] GenerateTangents(IList<Vector3> positions, IList<Vector3> normals, IList<Vector2> uvs, uint[] indices)
    {
        var tan1 = new Vector3[positions.Count];
        var tan2 = new Vector3[positions.Count];
        var tangents = new Vector4[positions.Count];

        for (int i = 0; i < indices.Length; i += 3)
        {
            int i1 = (int)indices[i];
            int i2 = (int)indices[i + 1];
            int i3 = (int)indices[i + 2];

            Vector3 v1 = positions[i1];
            Vector3 v2 = positions[i2];
            Vector3 v3 = positions[i3];

            Vector2 w1 = uvs[i1];
            Vector2 w2 = uvs[i2];
            Vector2 w3 = uvs[i3];

            float x1 = v2.X - v1.X;
            float x2 = v3.X - v1.X;
            float y1 = v2.Y - v1.Y;
            float y2 = v3.Y - v1.Y;
            float z1 = v2.Z - v1.Z;
            float z2 = v3.Z - v1.Z;

            float s1 = w2.X - w1.X;
            float s2 = w3.X - w1.X;
            float t1 = w2.Y - w1.Y;
            float t2 = w3.Y - w1.Y;

            float r = 1.0f / (s1 * t2 - s2 * t1);
            if (float.IsInfinity(r) || float.IsNaN(r)) r = 1.0f; // Safety

            Vector3 sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
            Vector3 tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z1 - s2 * z2) * r);

            tan1[i1] += sdir;
            tan1[i2] += sdir;
            tan1[i3] += sdir;
        
            tan2[i1] += tdir;
            tan2[i2] += tdir;
            tan2[i3] += tdir;
        }

        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 n = normals[i];
            Vector3 t = tan1[i];
        
            // Gram-Schmidt orthogonalization
            // Project t onto n and subtract it
            // T_ortho = T - N * dot(N, T)
            Vector3 tOrtho = t - n * Vector3.Dot(n, t);
            
            // Normalize result
            // Safety: If t was parallel to n, tOrtho is zero length.
            if (tOrtho.LengthSquared() < 0.0001f) 
            {
                tOrtho = Vector3.Cross(n, Vector3.UnitY);
                // fallback
                if (tOrtho.LengthSquared() < 0.0001f)
                    tOrtho = Vector3.Cross(n, Vector3.UnitZ);
            }
        
            t = Vector3.Normalize(tOrtho);
        
            float w = (Vector3.Dot(Vector3.Cross(n, t), tan2[i]) < 0.0f) ? -1.0f : 1.0f;
            tangents[i] = new Vector4(t, w);
        }
        return tangents;
    }
}
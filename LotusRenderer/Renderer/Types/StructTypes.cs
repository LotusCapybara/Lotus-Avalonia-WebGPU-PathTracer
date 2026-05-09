using System.Numerics;
using System.Runtime.InteropServices;

namespace LotusRenderer.Renderer.Types;

// All these types are meant to be shared with the GPU
// so they need to be 16 bytes aligned

[StructLayout(LayoutKind.Sequential)]
public struct ViewData
{
    public uint Width;
    public uint Height;
    public uint RenderMode; // for debugging and other things, not used on every kernel
    
    // could be packed with other flags 
    public uint viewIsMoving;
}

[StructLayout(LayoutKind.Sequential)]
public struct FilmData
{
    public uint screenWidth;
    public uint screenHeight;
    public float cameraExposure;
    public float bloomThreshold;
    public float bloomSoftKnee;
    public float bloomIntensity;
    public float bloomRadius;
    public float bloomSpreadPower;
    public float acesPower;
}

[StructLayout(LayoutKind.Sequential)]
public struct SdfSphere
{
    public Vector3 Position;
    public float Radius;
    
    public int materialId;
    public float _pad1;
    public float _pad2;
    public float _pad3;
}

[StructLayout(LayoutKind.Sequential)]
public struct SdfBox
{
    public Vector3 Center;
    public float _pad1;
    public Vector3 HalfExtents;
    public int materialId;
}

[StructLayout(LayoutKind.Sequential)]
public struct SceneData
{
    // 16
    public int QtyMaterials;
    public int QtySpheres;
    public int QtyBoxes;
    public float HdrIntensity;    
    
    // 16
    public EHdriDisplayMode HdriDisplayMode; 
    public float Orientation;
    public int QtyMeshes;
    public int QtyMeshInstances;
    
    // 16
    public int QtyEmissiveTriangles;
    public int TotalVertices;
    public int TotalTriIndices;
    public float TotalEmissiveArea;
    
    // 16
    // need to confirm this, but apparently in wgsl vec3 NEEDS to start aligned to 16b
    public Vector3 linearFogColor;
    public float linearFogDensity;
    
    // 16
    public float linearFogG;
    public float linearFogStartDist;
    public float fogNoiseScale;
    public float fogNoiseMin;
}

[StructLayout(LayoutKind.Sequential)]
public struct FrameData
{
    public uint FrameNumber;
    public uint Rnd;
    private float _pad2;
    private float _pad3;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackedVertex
{
    // UV are interlaced with Pos and Normal
    // to favor 16 byte alignment for Pos and Normal
    public Vector3 Position;
    public float U;          
    public Vector3 Normal;
    public float V;
    public Vector4 Tangent;
}

[StructLayout(LayoutKind.Sequential)]
public struct MeshInfo
{
    public Vector3 BoundMin;
    public uint tIndexOffset;
    
    public Vector3 BoundMax;
    public uint qtyVertices;

    public uint qtyTriIdx;
    public uint matId;
    public uint qtyBVHNodes;
    public uint treeRootIdx;
}

[StructLayout(LayoutKind.Sequential)]
public struct MeshInstance
{
    public Matrix4x4 Transform;        // Local -> World
    public Matrix4x4 InverseTransform; // World -> Local (Crucial for Ray Tracing)
    public uint MeshInfoId;            // Index into your MeshInfos buffer
    public float _pad1;
    public float _pad2;
    public float _pad3;
}

[StructLayout(LayoutKind.Sequential)]
public struct Material
{
    // surface basics
    public Vector4 BaseColor;          // (0 - 15)
    
    public float Metallic;              
    public float Roughness;             
    public float IOR;                   
    public float TransmissionWeight;   // (16 - 31)
    
    // sub-surface
    public Vector3 SSRadius;            
    public float SSWeight;             // (32 - 47) 
    
    public float SSScale;         
    public float SSAnisotropy;              
    public float _pad1;             
    public float _pad2;                // (48-63)   
    
    // specularity
    public Vector3 SpecularTint;        
    public float Anisotropy;           // (64-79)

    // coat
    public float CoatWeight;            
    public float CoatRoughness;           
    public float CoatIOR;               
    public uint matIndex;                // (80 - 95)
    
    public Vector3 CoatTint;           
    // sheen
    public float SheenWeight;          // (96 - 111)   
    
    public Vector3 SheenTint; 
    public float SheenRoughness;       // (112 - 127)
    
    // Emission
    public Vector3 EmissionColor;      
    public float EmissionStrength;     // (128 - 143)
    
    // Textures
    public int textIdBaseColor;
    public int textIdRoughness;
    public int textIdMetallic;
    public int textIdEmission;
    
    public int textIdNormal;
    public float normalScale;
    public int textIdTransmission;
    public uint blendType;
}

public enum EBlendType : uint
{
    // 0: Opaque, 1: Blend, 2: Mask in/off
    Opaque = 0, Blend = 1, Mask = 2
}


[StructLayout(LayoutKind.Sequential)]
public struct BVH2Node
{
    public Vector3 BoundMin;
    public int TriCount;
    public Vector3 BoundMax;
    
    // index of the left child, both are 
    // contiguous so right is this +1
    public int FirstChildIdx;
}

[StructLayout(LayoutKind.Sequential)]
public struct BloomStepParams
{
    public float srcWidth;
    public float srcHeight;
    public float dstWidth;
    public float dstHeight;
    public float lowWeight;
}

[StructLayout(LayoutKind.Sequential)]
public struct EmissiveTriangle
{
    public uint meshInstanceIdx;
    public uint triIndexOffset;
    public float area;
    public float _padding1;
    public Vector2 uv0;
    public Vector2 uv1;
    public Vector2 uv2;
    public float _padding2;
    public float _padding3;
}


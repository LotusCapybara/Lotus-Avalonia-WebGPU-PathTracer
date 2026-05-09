using System;
using System.Runtime.InteropServices;
using LotusRenderer.Renderer.Mesh;
using LotusRenderer.Renderer.Types;
using LotusRenderer.Renderer.World;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

namespace LotusRenderer.Renderer.Compute;

public unsafe class TraceBuffers : IDisposable
{
    // Buffers - uniform
    public GpuBuffer cameraData;
    public GpuBuffer viewData;
    public GpuBuffer sceneData;
    public GpuBuffer frameData;
    public GpuBuffer filmData;
    
    // Buffers - pixels
    public GpuBuffer output;
    public GpuBuffer staging;
    public GpuBuffer accumulation;
    
    // Scene Data
    public GpuBuffer? materials;
    public GpuBuffer? spheres;
    public GpuBuffer? boxes;
    public GpuBuffer? meshInfos;
    public GpuBuffer? meshInstances;
    public GpuBuffer? vertices;
    public GpuBuffer? triIndices;
    public GpuBuffer? blas;
    public GpuBuffer? tlas;
    public GpuBuffer? emissiveTriangles;
    
    // Textures
    public Texture* textureArrayMaterialMaps;
    public TextureView* textureArrayViewMaterialMaps;

    public Sampler* linearSampler;
    
    private WebGPU _api;
    private Device* _device;

    public TraceBuffers(Scene scene, Film film)
    {
        _api = LWGPU.Instance.Api;
        _device = LWGPU.Instance.Device;
        
        // buffers data and uniforms
        cameraData = GpuBuffer.CreateUniform((ulong)Marshal.SizeOf<CameraData>(), "CameraData");
        viewData = GpuBuffer.CreateUniform((ulong)Marshal.SizeOf<ViewData>(), "ViewData");
        sceneData = GpuBuffer.CreateUniform((ulong)Marshal.SizeOf<SceneData>(), "SceneData");
        frameData = GpuBuffer.CreateUniform((ulong)Marshal.SizeOf<FrameData>(), "FrameData");
        filmData  = GpuBuffer.CreateUniform((ulong)Marshal.SizeOf<FilmData>(), "FilmData");
        
        // buffers imaging, etc
        ulong u32ImgSize  = (ulong)(scene.Width * scene.Height * sizeof(uint));
        ulong fpImgSize   = (ulong)(scene.Width * scene.Height * sizeof(float) * 4);

        output = GpuBuffer.CreateStorage(u32ImgSize, "FrameOutput");
        staging = GpuBuffer.CreateStaging(fpImgSize, "Staging");
        accumulation = GpuBuffer.CreateStorage(fpImgSize, "Accumulation");

        WriteInitialData(scene, film);
        ResetTextureTargets(scene);
    }

    public void ResetTextureTargets(Scene scene)
    {
        ulong u32ImgSize  = (ulong)(scene.Width * scene.Height * sizeof(uint));
        
        UInt32[] blackDataU32 = new UInt32[u32ImgSize];
        Array.Fill<UInt32>(blackDataU32, 0);
        
        float[] blackDataFloat = new float[u32ImgSize];
        Array.Fill<float>(blackDataFloat, 0);
        
        using var writePass = new ComputePass();
        writePass.WriteBuffer(accumulation, blackDataFloat);
    }

    public void WriteInitialData(Scene scene, Film film)
    {
        // init buffers
        using var initPass = new ComputePass();
        
        initPass.WriteBuffer(this.viewData, [scene.ViewData]);
        initPass.WriteBuffer(cameraData, [CameraController.GetData()]);
        initPass.WriteBuffer(sceneData, [scene.Data]);
        initPass.WriteBuffer(frameData, [new FrameData()]);
        initPass.WriteBuffer(filmData, [film.Data]);
    }

    // create/recreate buffers based on the scene data and 
    // write them to the gpu
    public void SetSceneData(Scene scene)
    {
        // in case there is pre-existing data
        CleanSceneDataBuffersAndTextures();
        
        // buffers n-arrays of data (vertices, materials, etc)
        // to avoid gpu panicking for zero sized buffers, all these buffers
        // would have some dummy data if they are actually not used
        uint materialsSize = (uint)(scene.Materials.Length * Marshal.SizeOf<Material>());
        materials = GpuBuffer.CreateStorage(materialsSize, "Materials");
        
        uint spheresSize = (uint)(scene.Spheres.Length * Marshal.SizeOf<SdfSphere>());
        spheres = GpuBuffer.CreateStorage(spheresSize, "Spheres");
        
        uint boxesSize = (uint) (scene.Boxes.Length * Marshal.SizeOf<SdfBox>());
        boxes = GpuBuffer.CreateStorage(boxesSize, "Boxes");
        
        uint meshInfosSize = (uint) (scene.MeshInfos.Length * Marshal.SizeOf<MeshInfo>());
        meshInfos = GpuBuffer.CreateStorage(meshInfosSize, "Mesh Infos");
        
        uint meshInstancesSize = (uint) (scene.MeshInstances.Length * Marshal.SizeOf<MeshInstance>());
        meshInstances = GpuBuffer.CreateStorage(meshInstancesSize, "Mesh Instances");
        
        uint verticesSize = (uint) (scene.PackedVertices.Length * Marshal.SizeOf<PackedVertex>());
        vertices = GpuBuffer.CreateStorage(verticesSize, "Vertices");
        
        uint triIndicesSize = (uint) (scene.TriangleIndices.Length * Marshal.SizeOf<uint>());
        triIndices = GpuBuffer.CreateStorage(triIndicesSize, "Triangle Indices");
        
        uint blasSize = (uint) (scene.BLASNodes.Length * Marshal.SizeOf<BVH2Node>());
        blas = GpuBuffer.CreateStorage(Math.Max(blasSize, 16), "BLAS");
        
        uint tlasSize = (uint) (scene.TLASNodes.Length * Marshal.SizeOf<BVH2Node>());
        tlas = GpuBuffer.CreateStorage(Math.Max(tlasSize, 16), "TLAS");
        
        uint emissiveTrisSize = (uint) (scene.EmissiveTriangles.Length * Marshal.SizeOf<EmissiveTriangle>());
        emissiveTriangles = GpuBuffer.CreateStorage(Math.Max(emissiveTrisSize, (uint) Marshal.SizeOf<EmissiveTriangle>()), "Emissive Triangles");
        
        // write data to the scene data buffers
        using var writePass = new ComputePass();
        writePass.WriteBuffer(materials, scene.Materials);
        writePass.WriteBuffer(spheres, scene.Spheres);
        writePass.WriteBuffer(boxes, scene.Boxes);
        writePass.WriteBuffer(meshInfos, scene.MeshInfos);
        writePass.WriteBuffer(meshInstances, scene.MeshInstances);
        writePass.WriteBuffer(vertices, scene.PackedVertices);
        writePass.WriteBuffer(triIndices, scene.TriangleIndices);
        writePass.WriteBuffer(blas, scene.BLASNodes);
        writePass.WriteBuffer(tlas, scene.TLASNodes);
        writePass.WriteBuffer(emissiveTriangles, scene.EmissiveTriangles);
        
        // create texture buffers and set the data based on the scene
        CreateTextureArrays(scene);
    }
    
    private void CreateTextureArrays(Scene scene)
    {

        var textDesc = new TextureDescriptor
        {
            Size = new Extent3D(TextureLoader.TXT_WIDTH, TextureLoader.TXT_HEIGHT, scene.TextureLayersCount),
            Format = TextureFormat.Rgba8Unorm,
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            MipLevelCount = 1,
            SampleCount = 1,
            Label = (byte*) SilkMarshal.StringToPtr("Scene Texture Array"),
        };
        
        textureArrayMaterialMaps = _api.DeviceCreateTexture(_device, textDesc);
        
        var viewDesc = new TextureViewDescriptor
        {
            Format = TextureFormat.Rgba8Unorm,
            Dimension = TextureViewDimension.Dimension2DArray,
            Aspect = TextureAspect.All,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = (uint)scene.TextureLayersCount
        };
        
        textureArrayViewMaterialMaps = _api.TextureCreateView(textureArrayMaterialMaps, viewDesc);

        var imageCopyTexture = new ImageCopyTexture
        {
            Texture = textureArrayMaterialMaps,
            Aspect = TextureAspect.All
        };

        var layout = new TextureDataLayout
        {
            Offset = 0,
            BytesPerRow = TextureLoader.TXT_WIDTH * 4,
            RowsPerImage = TextureLoader.TXT_HEIGHT
        };

        var size = new Extent3D(TextureLoader.TXT_WIDTH, TextureLoader.TXT_HEIGHT, (uint)scene.TextureLayersCount);
        fixed (byte* dataPtr = scene.RawTextureArray)
        {
            _api.QueueWriteTexture(
                _api.DeviceGetQueue(_device), 
                &imageCopyTexture,
                dataPtr, 
                (nuint)scene.RawTextureArray.Length, 
                &layout, 
                &size);
        }
        
        SamplerDescriptor samplerDesc = new SamplerDescriptor
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            MaxAnisotropy = 1
        };
        
        linearSampler = _api.DeviceCreateSampler(_device, &samplerDesc);
        
        
        SilkMarshal.Free((nint)textDesc.Label);
    }

    public void CleanSceneDataBuffersAndTextures()
    {
        materials?.Dispose();
        materials = null;
        
        spheres?.Dispose();
        spheres = null;
        
        boxes?.Dispose();
        boxes = null;
        
        meshInfos?.Dispose();
        meshInfos = null;
        
        meshInstances?.Dispose();
        meshInstances = null;
        
        vertices?.Dispose();
        vertices = null;
        
        triIndices?.Dispose();
        triIndices = null;
        
        blas?.Dispose();
        blas = null;
        
        tlas?.Dispose();
        tlas = null;
        
        emissiveTriangles?.Dispose();
        emissiveTriangles = null;
        
        // Textures
        if (textureArrayViewMaterialMaps != null)
        {
            _api.TextureViewRelease(textureArrayViewMaterialMaps);
            textureArrayViewMaterialMaps = null;
        }


        if (textureArrayMaterialMaps != null)
        {
            _api.TextureRelease(textureArrayMaterialMaps);
            textureArrayMaterialMaps = null;
        }

    }
    
    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
            _disposed = true;    
        
        // buffers
        cameraData.Dispose();
        viewData.Dispose();
        sceneData.Dispose();
        frameData.Dispose();
        filmData.Dispose();
        output.Dispose();
        staging.Dispose();
        accumulation.Dispose();
        
        _api.SamplerRelease(linearSampler);

        CleanSceneDataBuffersAndTextures();
    }
}
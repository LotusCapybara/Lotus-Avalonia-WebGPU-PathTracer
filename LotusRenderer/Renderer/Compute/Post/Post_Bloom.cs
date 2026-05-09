using System;
using System.Runtime.InteropServices;
using LotusRenderer.Renderer.Types;
using LotusRenderer.Renderer.World;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

namespace LotusRenderer.Renderer.Compute.Post;

public unsafe class Post_Bloom : IDisposable
{
    private const int PYRAMID_LEVELS = 7;
    
    private BindGroupLayout* _layoutPostPo;
    private BindGroupLayout* _layoutBloom;
    
    private PipelineLayout* _pipelineLayout;
    
    
    private GpuComputePipeline _pipelinePrefilter;
    private GpuComputePipeline _pipelineDownSample;
    private GpuComputePipeline _pipelineBlur;
    private GpuComputePipeline _pipelineUpSample;
    private GpuComputePipeline _pipelineApply;
    
    private BindGroup* _bindGroupInputOutput;
    
    private Device* _device;

    private PostProcessing _pp;
    private Scene _scene;
    private BindGroups _globalBinds;
    private TraceBuffers _traceBuffers;

    private Texture* _bloomTextureA;
    private Texture* _bloomTextureB;
    private TextureView*[] _bloomViewsA;
    private TextureView*[] _bloomViewsB;

    private GpuBuffer _bfBloomStepParams;

    public Post_Bloom(PostProcessing postPro, TraceBuffers traceBuffers, Scene scene, BindGroups globalBinds)
    {
        _pp = postPro;
        _scene = scene;
        _globalBinds = globalBinds;
        _traceBuffers = traceBuffers;
        _device = LWGPU.Instance.Device;
        
        _bfBloomStepParams = GpuBuffer.CreateUniform((ulong)Marshal.SizeOf<BloomStepParams>(), "BloomStepParams");
        

        CreateTextures();
        MakePipelines();
        MakeBindGroups();
    }

    private void MakePipelines()
    {
        // group 1
        _layoutPostPo = new LayoutBuilder()
            .AddStorage(0, false)   // inputColor: array<vec4<f32>>;
            .AddStorage(1, false)   // outputColor: array<vec4<f32>>;
            .Build(_device);
        
        // group 2
        _layoutBloom = new LayoutBuilder()
            .AddUniform(0)   // bloomStep   : BloomStepParams;
            .AddTexture(1)   // textSrc   : texture_2d<f32>;
            .AddTexture(2)   // textSrc2   : texture_2d<f32>;
            .AddStorageTexture(3, TextureFormat.Rgba16float) // textDst  : texture_storage_2d<rgba16float, write>;
            .AddSampler(4)   // textSampler : sampler;
            .Build(_device);
        
        var layouts = stackalloc BindGroupLayout*[] 
        {
            _globalBinds.layout_uniforms,
            _layoutPostPo,
            _layoutBloom
        };
        
        var pipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayouts = layouts,
            BindGroupLayoutCount = 3 
        };
        
        _pipelineLayout = LWGPU.Instance.Api
            .DeviceCreatePipelineLayout(LWGPU.Instance.Device, pipelineLayoutDesc);
        
        _pipelinePrefilter = GpuComputePipeline.FromEmbedded("post_bloom", _pipelineLayout, "kernel_bloom_prefilter");
        _pipelineDownSample = GpuComputePipeline.FromEmbedded("post_bloom", _pipelineLayout, "kernel_down_sample");
        _pipelineBlur = GpuComputePipeline.FromEmbedded("post_bloom", _pipelineLayout, "kernel_blur");
        _pipelineUpSample = GpuComputePipeline.FromEmbedded("post_bloom", _pipelineLayout, "kernel_up_sample");
        _pipelineApply = GpuComputePipeline.FromEmbedded("post_bloom", _pipelineLayout, "kernel_bloom_apply");
    }

    private void MakeBindGroups()
    {
        _bindGroupInputOutput = BindGroupBuilder
            .Begin(_layoutPostPo)
            .AddBuffer(0, _pp.bfColorInput)     
            .AddBuffer(1, _pp.bfColorOutput)    
            .Build("Final Packing");
    }
    
    private void CreateTextures()
    {
        var api = LWGPU.Instance.Api;
        
        var textSampleDesc = new TextureDescriptor
        {
            Size = new Extent3D(_scene.Width, _scene.Height, 1),
            Format = TextureFormat.Rgba16float,
            Usage = TextureUsage.TextureBinding | TextureUsage.StorageBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            MipLevelCount = PYRAMID_LEVELS,
            SampleCount = 1,
            Label = (byte*) SilkMarshal.StringToPtr("Sampling Texture"),
        };
        
        _bloomTextureA = api.DeviceCreateTexture(_device, textSampleDesc);
        _bloomTextureB = api.DeviceCreateTexture(_device, textSampleDesc);
        
        // PYRAMID_LEVELS views, 1 for each mip map level of the downsample/upsample process
        // 0 is the base texture, and it reaches index PYRAMID_LEVELS - 1 as lowest sample level
        _bloomViewsA = new TextureView*[PYRAMID_LEVELS];
        _bloomViewsB = new TextureView*[PYRAMID_LEVELS];
        
        for (int i = 0; i < PYRAMID_LEVELS; i++)
        {
            var viewDesc = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba16float,
                Dimension = TextureViewDimension.Dimension2D,
                Aspect = TextureAspect.All,
                BaseMipLevel = (uint) i,
                MipLevelCount = 1,
                BaseArrayLayer = 0,
                ArrayLayerCount = 1
            };
        
            _bloomViewsA[i] = api.TextureCreateView(_bloomTextureA, viewDesc);
            _bloomViewsB[i] = api.TextureCreateView(_bloomTextureB, viewDesc);
        } 
        
        SilkMarshal.Free((nint)textSampleDesc.Label);
    }

    public void Execute()
    {
        Execute_PreFilter();
        Execute_DownSample();
        Execute_Blur();
        Execute_UpSample();
        Execute_Apply();
    }

    private void Execute_PreFilter()
    {
        uint workgroupsX = (_scene.Width + 7) / 8;
        uint workgroupsY = (_scene.Height + 7) / 8;
        
        var bindGroupBloom = BindGroupBuilder
            .Begin(_layoutBloom)
            .AddBuffer(0, _bfBloomStepParams)     
            .AddTexture(1, _bloomViewsB[0]) // as dummy
            .AddTexture(2, _bloomViewsB[1]) // as dummy
            .AddTexture(3, _bloomViewsA[0]) // textDst
            .AddSampler(4, _traceBuffers.linearSampler)
            .Build("Pre filter");
        
        // pre filter, grabs the luminance and write to a texture so it can be used with a sampler
        // during down sampling
        using var finalPackPass = new ComputePass();
        finalPackPass.Begin();
        finalPackPass.SetPipeline(_pipelinePrefilter);
        finalPackPass.SetBindGroup(0, _globalBinds.bindGroup_globalUniforms);
        finalPackPass.SetBindGroup(1, _bindGroupInputOutput);
        finalPackPass.SetBindGroup(2, bindGroupBloom);
        finalPackPass.Dispatch(workgroupsX, workgroupsY, 1);
        finalPackPass.End();
        finalPackPass.Submit("Bloom Pre Filter");
        
        var api = LWGPU.Instance.Api;
        api.BindGroupRelease(bindGroupBloom);
    }

    private void Execute_DownSample()
    {
        var api = LWGPU.Instance.Api;
        
        for (int i = 0; i < PYRAMID_LEVELS - 1; i++)
        {
            // dest texture size is being used to calculate the dispatch size
            // (the size of the target smaller texture)
            uint srcW = Math.Max(1, _scene.Width  >> i);
            uint srcH = Math.Max(1, _scene.Height >> i);
            uint dstW = Math.Max(1, _scene.Width  >> (i + 1));
            uint dstH = Math.Max(1, _scene.Height >> (i + 1));
            uint workgroupsX = (dstW + 7) / 8;
            uint workgroupsY = (dstH + 7) / 8;
            
            var step = new BloomStepParams
            {
                srcWidth  = srcW,
                srcHeight = srcH,
                dstWidth  = dstW,
                dstHeight = dstH,
            };

            using (var writePass = new ComputePass())
            {
                writePass.Begin();
                writePass.End();
                writePass.WriteBuffer(_bfBloomStepParams, [step]);
                writePass.Submit($"Bloom Blur Params: {i}");
            }
            
            var bindGroupDownSample = BindGroupBuilder
                .Begin(_layoutBloom)
                .AddBuffer(0, _bfBloomStepParams)     
                .AddTexture(1, _bloomViewsA[i])
                .AddTexture(2, _bloomViewsA[0])   // dummy
                .AddTexture(3, _bloomViewsA[i + 1])   // writes next downsampled level
                .AddSampler(4, _traceBuffers.linearSampler)
                .Build("Bur Levels");
            
            using var finalPackPass = new ComputePass();
            finalPackPass.Begin();
            finalPackPass.SetPipeline(_pipelineDownSample);
            finalPackPass.SetBindGroup(0, _globalBinds.bindGroup_globalUniforms);
            finalPackPass.SetBindGroup(1, _bindGroupInputOutput);
            finalPackPass.SetBindGroup(2, bindGroupDownSample);
            finalPackPass.Dispatch(workgroupsX, workgroupsY, 1);
            finalPackPass.End();
            finalPackPass.Submit($"Bloom Blur {i}");
        
            api.BindGroupRelease(bindGroupDownSample);
        }
    }

    private void Execute_Blur()
    {
        var api = LWGPU.Instance.Api;
    
        for (int i = 0; i < PYRAMID_LEVELS - 1; i++)
        {
            uint width = Math.Max(1u, _scene.Width  >> i);
            uint height = Math.Max(1u, _scene.Height >> i);
            uint workgroupsX = (width + 7) / 8;
            uint workgroupsY = (height + 7) / 8;
    
            var step = new BloomStepParams
            {
                srcWidth  = width,
                srcHeight = height,
                dstWidth  = width,
                dstHeight = height,
            };
    
            using (var writePass = new ComputePass())
            {
                writePass.Begin();
                writePass.End();
                writePass.WriteBuffer(_bfBloomStepParams, [step]);
                writePass.Submit($"Bloom Blur Params: {i}");
            }
    
            // src = smaller mip, dst = bigger mip
            var bindGroupBlur = BindGroupBuilder
                .Begin(_layoutBloom)
                .AddBuffer(0, _bfBloomStepParams)
                .AddTexture(1, _bloomViewsA[i]) // textSrc
                .AddTexture(2, _bloomViewsA[i + 1]) // textSrc2 as dummy
                .AddTexture(3, _bloomViewsB[i])     // textDst writes same A to B but blurred, per level
                .AddSampler(4, _traceBuffers.linearSampler)
                .Build($"Bloom Blur {i}");
    
            using var upPass = new ComputePass();
            upPass.Begin();
            upPass.SetPipeline(_pipelineBlur);               
            upPass.SetBindGroup(0, _globalBinds.bindGroup_globalUniforms);
            upPass.SetBindGroup(1, _bindGroupInputOutput);
            upPass.SetBindGroup(2, bindGroupBlur);
            upPass.Dispatch(workgroupsX, workgroupsY, 1);
            upPass.End();
            upPass.Submit($"Bloom Blur Dispatch: {i}");
    
            api.BindGroupRelease(bindGroupBlur);
        }
    }
    
    private void Execute_UpSample()
    {
        var api = LWGPU.Instance.Api;

        // Go from smallest mip back up
        for (int i = PYRAMID_LEVELS - 2; i >= 0; i--)
        {
            uint dstW = Math.Max(1u, _scene.Width  >> i);
            uint dstH = Math.Max(1u, _scene.Height >> i);
            uint srcW = Math.Max(1u, _scene.Width  >> (i + 1));
            uint srcH = Math.Max(1u, _scene.Height >> (i + 1));
            uint workgroupsX = (dstW + 7) / 8;
            uint workgroupsY = (dstH + 7) / 8;

            float baseSpread = 0.2f;
            float spreadDepth = PYRAMID_LEVELS - 1 - i;
            
            var step = new BloomStepParams
            {
                srcWidth  = srcW,
                srcHeight = srcH,
                dstWidth  = dstW,
                dstHeight = dstH,
                // a curve of spread weight based on how far in the pyramid this level is
                lowWeight =  baseSpread * (1f + spreadDepth * 0.2f), 
            };

            using (var writePass = new ComputePass())
            {
                writePass.Begin();
                writePass.End();
                writePass.WriteBuffer(_bfBloomStepParams, [step]);
                writePass.Submit($"Bloom UpSample Params: {i}");
            }

            // src = smaller mip, dst = bigger mip
            var bindGroupUpSample = BindGroupBuilder
                .Begin(_layoutBloom)
                .AddBuffer(0, _bfBloomStepParams)
                .AddTexture(1, _bloomViewsA[i + 1]) // textSrc
                .AddTexture(2, _bloomViewsB[i + 1])     // textSrc2
                .AddTexture(3, _bloomViewsA[i])     // textDst
                .AddSampler(4, _traceBuffers.linearSampler)
                .Build($"Bloom UpSample {i}");

            using var upPass = new ComputePass();
            upPass.Begin();
            upPass.SetPipeline(_pipelineUpSample);               
            upPass.SetBindGroup(0, _globalBinds.bindGroup_globalUniforms);
            upPass.SetBindGroup(1, _bindGroupInputOutput);
            upPass.SetBindGroup(2, bindGroupUpSample);
            upPass.Dispatch(workgroupsX, workgroupsY, 1);
            upPass.End();
            upPass.Submit($"Bloom UpSample Dispatch: {i}");

            api.BindGroupRelease(bindGroupUpSample);
        }
    }


    private void Execute_Apply()
    {
        uint workgroupsX = (_scene.Width + 7) / 8;
        uint workgroupsY = (_scene.Height + 7) / 8;
        
        var bindGroupBloom = BindGroupBuilder
            .Begin(_layoutBloom)
            .AddBuffer(0, _bfBloomStepParams)     
            .AddTexture(1, _bloomViewsA[0]) // 
            .AddTexture(2, _bloomViewsB[1]) // as dummy
            .AddTexture(3, _bloomViewsB[0]) // as dummy
            .AddSampler(4, _traceBuffers.linearSampler)
            .Build("Bloom Final Apply");
        
        // pre filter, grabs the luminance and write to a texture so it can be used with a sampler
        // during down sampling
        using var finalPackPass = new ComputePass();
        finalPackPass.Begin();
        finalPackPass.SetPipeline(_pipelineApply);
        finalPackPass.SetBindGroup(0, _globalBinds.bindGroup_globalUniforms);
        finalPackPass.SetBindGroup(1, _bindGroupInputOutput);
        finalPackPass.SetBindGroup(2, bindGroupBloom);
        finalPackPass.Dispatch(workgroupsX, workgroupsY, 1);
        finalPackPass.End();
        finalPackPass.CopyBufferToBuffer(_pp.bfColorOutput, _pp.bfColorInput, _pp.bfColorOutput.Size);
        finalPackPass.Submit("Bloom Final Apply");
        
        var api = LWGPU.Instance.Api;
        api.BindGroupRelease(bindGroupBloom);
    }

    public void Dispose()
    {
        var api = LWGPU.Instance.Api;
        
        _pipelinePrefilter.Dispose();
        _pipelineDownSample.Dispose();
        _pipelineBlur.Dispose();
        _pipelineUpSample.Dispose();
        _pipelineApply.Dispose();
        
        api.PipelineLayoutRelease(_pipelineLayout);
        
        api.BindGroupLayoutRelease(_layoutBloom);
        api.BindGroupLayoutRelease(_layoutPostPo);
        api.BindGroupRelease(_bindGroupInputOutput);

        if (_bloomTextureA != null)
        {
            api.TextureRelease(_bloomTextureA);
            _bloomTextureA = null;
        }

        if (_bloomTextureB != null)
        {
            api.TextureRelease(_bloomTextureB);
            _bloomTextureB = null;
        }
        
        for (int i = 0; i < _bloomViewsA.Length; i++)
        {
            api.TextureViewRelease(_bloomViewsA[i]);
            api.TextureViewRelease(_bloomViewsB[i]);
        }
        
        _bloomViewsA = null;
        _bloomViewsB = null;
        _bfBloomStepParams.Dispose();
    }
}
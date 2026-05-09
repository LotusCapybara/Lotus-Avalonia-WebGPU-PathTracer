using System;
using LotusRenderer.Renderer.World;
using Silk.NET.WebGPU;

namespace LotusRenderer.Renderer.Compute.Post;

public unsafe class Post_PackToFinal : IDisposable
{
    private BindGroupLayout* _bindGroupLayout;
    private BindGroup* _bindGroup;
    private PipelineLayout* _pipelineLayout;
    private GpuComputePipeline _pipeline;
    
    private Device* _device;

    private PostProcessing _pp;
    private TraceBuffers _traceBuffers;
    private Scene _scene;
    private BindGroups _globalBinds;
    
    public Post_PackToFinal(PostProcessing postPro, TraceBuffers traceBuffers, Scene scene, BindGroups globalBinds)
    {
        _pp = postPro;
        _traceBuffers = traceBuffers;
        _scene = scene;
        _globalBinds = globalBinds;
        _device = LWGPU.Instance.Device;
        
        MakeBindGroups();
        MakePipeline();
    }

    private void MakePipeline()
    {
        var layouts = stackalloc BindGroupLayout*[] 
        {
            _globalBinds.layout_uniforms,
            _bindGroupLayout
        };
        
        var pipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayouts = layouts,
            BindGroupLayoutCount = 2 
        };
        
        _pipelineLayout = LWGPU.Instance.Api
            .DeviceCreatePipelineLayout(LWGPU.Instance.Device, pipelineLayoutDesc);
        
        _pipeline = GpuComputePipeline.FromEmbedded("post_final_packing", _pipelineLayout, "kernel_final_pack");
    }

    private void MakeBindGroups()
    {
        _bindGroupLayout = new LayoutBuilder()
            
            .AddStorage(0, false)   // inputColor: array<vec4<f32>>;
            .AddStorage(1, false)   // outputTexture: texture_storage_2d<rgba16float, write>;
            .Build(_device);

        _bindGroup = BindGroupBuilder
            .Begin(_bindGroupLayout)
            .AddBuffer(0, _pp.bfColorInput)      // inputColor: array<vec4<f32>>;
            .AddBuffer(1, _pp.bfPackedOutput)    // packedOutput: array<u32>;
            .Build("Final Packing");
    }

    // in: pp.bfColorInput
    // out: pp.bfPackedOutput
    public void Execute()
    {
        uint workgroupsX = (_scene.Width + 7) / 8;
        uint workgroupsY = (_scene.Height + 7) / 8;
        
        using var finalPackPass = new ComputePass();
        finalPackPass.Begin();
        finalPackPass.SetPipeline(_pipeline);
        finalPackPass.SetBindGroup(0, _globalBinds.bindGroup_globalUniforms);
        finalPackPass.SetBindGroup(1, _bindGroup);
        finalPackPass.Dispatch(workgroupsX, workgroupsY, 1);
        finalPackPass.End();
        finalPackPass.CopyBufferToBuffer(_pp.bfPackedOutput, _traceBuffers.staging, _pp.bfPackedOutput.Size);
        finalPackPass.Submit("PP Final Pack");
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        var api = LWGPU.Instance.Api;
        api.PipelineLayoutRelease(_pipelineLayout);
        api.BindGroupLayoutRelease(_bindGroupLayout);
        api.BindGroupRelease(_bindGroup);
    }
}
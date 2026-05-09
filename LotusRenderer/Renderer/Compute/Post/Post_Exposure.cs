using System;
using LotusRenderer.Renderer.World;
using Silk.NET.WebGPU;

namespace LotusRenderer.Renderer.Compute.Post;

public unsafe class Post_Exposure : IDisposable
{
    private BindGroupLayout* _bindGroupLayout;
    private BindGroup* _bindGroup;
    private PipelineLayout* _pipelineLayout;
    private GpuComputePipeline _pipeline;
    
    private Device* _device;

    private PostProcessing _pp;
    private Scene _scene;
    private BindGroups _globalBinds;
    
    public Post_Exposure(PostProcessing postPro, Scene scene, BindGroups globalBinds)
    {
        _pp = postPro;
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
        
        _pipeline = GpuComputePipeline.FromEmbedded("post_exposure", _pipelineLayout, "kernel_exposure");
    }

    private void MakeBindGroups()
    {
        _bindGroupLayout = new LayoutBuilder()
            
            .AddStorage(0, false)   // inputColor: array<vec4<f32>>;
            .AddStorage(1, false)   // outputColor: array<vec4<f32>>;
            .Build(_device);

        _bindGroup = BindGroupBuilder
            .Begin(_bindGroupLayout)
            .AddBuffer(0, _pp.bfColorInput)     
            .AddBuffer(1, _pp.bfColorOutput)    
            .Build("Final Packing");
    }

    // in: pp.bfColorInput
    // out: pp.bfColorOutput
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
        finalPackPass.CopyBufferToBuffer(_pp.bfColorOutput, _pp.bfColorInput, _pp.bfColorOutput.Size);
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